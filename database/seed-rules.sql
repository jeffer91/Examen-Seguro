INSERT INTO monitoring_rules(exam_id,kind,pattern,label,severity,enabled,capture_on_match)
SELECT NULL,v.kind,v.pattern,v.label,v.severity,true,true
FROM (VALUES
 ('domain','chatgpt.com','ChatGPT','high'),
 ('domain','gemini.google.com','Gemini','high'),
 ('domain','kimi.com','Kimi','high'),
 ('domain','copilot.microsoft.com','Microsoft Copilot','high'),
 ('domain','claude.ai','Claude','high'),
 ('domain','deepseek.com','DeepSeek','high'),
 ('domain','perplexity.ai','Perplexity','high'),
 ('domain','grok.com','Grok','high'),
 ('domain','poe.com','Poe','high'),
 ('process','taskmgr.exe','Administrador de tareas','critical'),
 ('process','explorer.exe','Explorador de archivos','medium'),
 ('process','chrome.exe','Google Chrome','medium'),
 ('process','msedge.exe','Microsoft Edge','medium'),
 ('process','firefox.exe','Mozilla Firefox','medium'),
 ('process','brave.exe','Brave','medium'),
 ('process','chatgpt.exe','Aplicación ChatGPT','high'),
 ('process','copilot.exe','Aplicación Copilot','high'),
 ('folder','*','Navegación por carpetas','medium')
) AS v(kind,pattern,label,severity)
WHERE NOT EXISTS (
    SELECT 1 FROM monitoring_rules r
    WHERE r.exam_id IS NULL
      AND r.kind=v.kind
      AND lower(r.pattern)=lower(v.pattern)
);
