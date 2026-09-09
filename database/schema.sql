CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS app_users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email text NOT NULL UNIQUE,
    display_name text NOT NULL,
    role text NOT NULL CHECK (role IN ('admin','veedor')),
    active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS exams (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL,
    career text,
    starts_at timestamptz,
    ends_at timestamptz,
    status text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft','active','closed')),
    created_by uuid REFERENCES app_users(id),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS devices (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    device_code text NOT NULL UNIQUE,
    hostname text,
    windows_version text,
    agent_version text,
    last_seen_at timestamptz,
    active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS students (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    student_code text NOT NULL UNIQUE,
    full_name text NOT NULL,
    career text,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS exam_assignments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    exam_id uuid NOT NULL REFERENCES exams(id) ON DELETE CASCADE,
    student_id uuid NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    device_id uuid NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    status text NOT NULL DEFAULT 'ready' CHECK (status IN ('ready','armed','active','finished')),
    consent_recorded boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (exam_id, student_id)
);

CREATE TABLE IF NOT EXISTS exam_sessions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    exam_id uuid NOT NULL REFERENCES exams(id) ON DELETE CASCADE,
    student_id uuid NOT NULL REFERENCES students(id),
    device_id uuid NOT NULL REFERENCES devices(id),
    status text NOT NULL DEFAULT 'ready' CHECK (status IN ('ready','active','finished','disconnected')),
    consent_acknowledged_at timestamptz,
    started_at timestamptz,
    finished_at timestamptz,
    last_heartbeat_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS monitoring_rules (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    exam_id uuid REFERENCES exams(id) ON DELETE CASCADE,
    kind text NOT NULL CHECK (kind IN ('process','domain','folder','focus','service')),
    pattern text NOT NULL,
    label text NOT NULL,
    severity text NOT NULL DEFAULT 'medium' CHECK (severity IN ('low','medium','high','critical')),
    enabled boolean NOT NULL DEFAULT true,
    capture_on_match boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS events (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id uuid NOT NULL REFERENCES exam_sessions(id) ON DELETE CASCADE,
    occurred_at timestamptz NOT NULL,
    event_type text NOT NULL,
    severity text NOT NULL DEFAULT 'info' CHECK (severity IN ('info','low','medium','high','critical')),
    process_name text,
    domain text,
    url text,
    folder_path text,
    duration_ms integer,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS screenshots (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id uuid NOT NULL UNIQUE REFERENCES events(id) ON DELETE CASCADE,
    storage_key text NOT NULL,
    sha256 text NOT NULL,
    width integer,
    height integer,
    image_bytes bytea,
    content_type text NOT NULL DEFAULT 'image/jpeg',
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS heartbeats (
    id bigserial PRIMARY KEY,
    session_id uuid NOT NULL REFERENCES exam_sessions(id) ON DELETE CASCADE,
    received_at timestamptz NOT NULL DEFAULT now(),
    agent_status text NOT NULL DEFAULT 'online'
);

CREATE INDEX IF NOT EXISTS idx_assignments_device_status ON exam_assignments(device_id,status);
CREATE INDEX IF NOT EXISTS idx_sessions_exam_status ON exam_sessions(exam_id,status);
CREATE INDEX IF NOT EXISTS idx_events_session_time ON events(session_id,occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_events_severity_time ON events(severity,occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_heartbeat_session_time ON heartbeats(session_id,received_at DESC);

-- Las reglas institucionales iniciales se administran desde el panel y se almacenan
-- en monitoring_rules. No se incluyen secretos ni DATABASE_URL en este archivo.
