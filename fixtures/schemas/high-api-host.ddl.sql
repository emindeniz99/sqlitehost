CREATE TABLE pending_host_calls (
    queue_id INTEGER PRIMARY KEY AUTOINCREMENT,
    call_id TEXT NOT NULL UNIQUE,
    method TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending'
);

CREATE TABLE script_inputs (
    name TEXT NOT NULL PRIMARY KEY,
    value_type TEXT NOT NULL,
    int_value INTEGER,
    real_value REAL,
    text_value TEXT,
    blob_value BLOB
);

CREATE TABLE script_vars (
    name TEXT NOT NULL PRIMARY KEY,
    value_type TEXT NOT NULL,
    int_value INTEGER,
    real_value REAL,
    text_value TEXT,
    blob_value BLOB
);

CREATE TABLE script_control (
    action TEXT NOT NULL,
    message TEXT
);

CREATE TABLE call_new_feature (
    call_id TEXT NOT NULL PRIMARY KEY,
    input_key TEXT NOT NULL
);

CREATE TABLE result_new_feature (
    call_id TEXT NOT NULL PRIMARY KEY,
    status TEXT NOT NULL DEFAULT 'done',
    result_ok INTEGER NOT NULL
);

CREATE TRIGGER trg_call_new_feature_queue
AFTER INSERT ON call_new_feature
BEGIN
    INSERT INTO pending_host_calls (call_id, method)
    VALUES (NEW.call_id, 'newFeature');
END;
