-- Esquema de referencia. La base Neon del proyecto ITSQMET Examen Seguro ya fue inicializada.
CREATE EXTENSION IF NOT EXISTS pgcrypto;
-- Tablas: app_users, exams, devices, students, exam_assignments, exam_sessions,
-- monitoring_rules, events, screenshots, heartbeats.
-- No guardar secretos ni cadenas DATABASE_URL en el repositorio.
