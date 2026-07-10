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

CREATE TABLE call_get_value (
    call_id TEXT NOT NULL PRIMARY KEY,
    input_key TEXT NOT NULL
);

CREATE TABLE result_get_value (
    call_id TEXT NOT NULL PRIMARY KEY,
    status TEXT NOT NULL DEFAULT 'done',
    result_value INTEGER NOT NULL
);

CREATE TRIGGER trg_call_get_value_queue
AFTER INSERT ON call_get_value
BEGIN
    INSERT INTO pending_host_calls (call_id, method)
    VALUES (NEW.call_id, 'getValue');
END;

CREATE TABLE call_set_value (
    call_id TEXT NOT NULL PRIMARY KEY,
    input_key TEXT NOT NULL,
    input_value INTEGER NOT NULL
);

CREATE TABLE result_set_value (
    call_id TEXT NOT NULL PRIMARY KEY,
    status TEXT NOT NULL DEFAULT 'done',
    result_success INTEGER NOT NULL
);

CREATE TRIGGER trg_call_set_value_queue
AFTER INSERT ON call_set_value
BEGIN
    INSERT INTO pending_host_calls (call_id, method)
    VALUES (NEW.call_id, 'setValue');
END;

CREATE TABLE call_get_values (
    call_id TEXT NOT NULL PRIMARY KEY,
    input_default_value INTEGER
);

CREATE TABLE call_get_values__input_keys (
    call_id TEXT NOT NULL,
    item_index INTEGER NOT NULL,
    input_key TEXT NOT NULL,
    PRIMARY KEY (call_id, item_index)
);

CREATE TABLE result_get_values (
    call_id TEXT NOT NULL PRIMARY KEY,
    status TEXT NOT NULL DEFAULT 'done'
);

CREATE TABLE result_get_values__result_entries (
    call_id TEXT NOT NULL,
    item_index INTEGER NOT NULL,
    result_key TEXT NOT NULL,
    result_value INTEGER NOT NULL,
    result_found INTEGER NOT NULL,
    PRIMARY KEY (call_id, item_index)
);

CREATE TRIGGER trg_call_get_values_queue
AFTER INSERT ON call_get_values
BEGIN
    INSERT INTO pending_host_calls (call_id, method)
    VALUES (NEW.call_id, 'getValues');
END;

CREATE TABLE call_put_blob (
    call_id TEXT NOT NULL PRIMARY KEY,
    input_key TEXT NOT NULL,
    input_data BLOB NOT NULL,
    input_note TEXT
);

CREATE TABLE result_put_blob (
    call_id TEXT NOT NULL PRIMARY KEY,
    status TEXT NOT NULL DEFAULT 'done',
    result_stored INTEGER NOT NULL
);

CREATE TRIGGER trg_call_put_blob_queue
AFTER INSERT ON call_put_blob
BEGIN
    INSERT INTO pending_host_calls (call_id, method)
    VALUES (NEW.call_id, 'putBlob');
END;

CREATE TABLE call_record_score (
    call_id TEXT NOT NULL PRIMARY KEY,
    input_key TEXT NOT NULL,
    input_score REAL NOT NULL,
    input_weight REAL
);

CREATE TABLE result_record_score (
    call_id TEXT NOT NULL PRIMARY KEY,
    status TEXT NOT NULL DEFAULT 'done',
    result_average REAL NOT NULL
);

CREATE TRIGGER trg_call_record_score_queue
AFTER INSERT ON call_record_score
BEGIN
    INSERT INTO pending_host_calls (call_id, method)
    VALUES (NEW.call_id, 'recordScore');
END;
