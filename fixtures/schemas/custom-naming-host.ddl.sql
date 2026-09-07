CREATE TABLE host_queue (
    seq INTEGER PRIMARY KEY AUTOINCREMENT,
    correlation_id TEXT NOT NULL UNIQUE,
    op_name TEXT NOT NULL,
    state TEXT NOT NULL DEFAULT 'pending'
);

CREATE TABLE host_inputs (
    var_name TEXT NOT NULL PRIMARY KEY,
    kind TEXT NOT NULL,
    i_val INTEGER,
    r_val REAL,
    t_val TEXT,
    b_val BLOB
);

CREATE TABLE host_scratch (
    var_name TEXT NOT NULL PRIMARY KEY,
    kind TEXT NOT NULL,
    i_val INTEGER,
    r_val REAL,
    t_val TEXT,
    b_val BLOB
);

CREATE TABLE host_control (
    verb TEXT NOT NULL,
    detail TEXT
);

CREATE TABLE req_lookup (
    correlation_id TEXT NOT NULL PRIMARY KEY,
    arg_lookup_key TEXT NOT NULL
);

CREATE TABLE resp_lookup (
    correlation_id TEXT NOT NULL PRIMARY KEY,
    state TEXT NOT NULL DEFAULT 'settled',
    ret_payload INTEGER NOT NULL
);

CREATE TRIGGER trg_req_lookup_queue
AFTER INSERT ON req_lookup
BEGIN
    INSERT INTO host_queue (correlation_id, op_name)
    VALUES (NEW.correlation_id, 'lookup');
END;

CREATE TABLE req_bulk_lookup (
    correlation_id TEXT NOT NULL PRIMARY KEY
);

CREATE TABLE req_bulk_lookup__arg_wanted_keys (
    correlation_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL,
    arg_lookup_key TEXT NOT NULL,
    PRIMARY KEY (correlation_id, ordinal)
);

CREATE TABLE resp_bulk_lookup (
    correlation_id TEXT NOT NULL PRIMARY KEY,
    state TEXT NOT NULL DEFAULT 'settled'
);

CREATE TABLE resp_bulk_lookup__ret_found_rows (
    correlation_id TEXT NOT NULL,
    ordinal INTEGER NOT NULL,
    ret_lookup_key TEXT NOT NULL,
    ret_payload INTEGER NOT NULL,
    ret_was_found INTEGER NOT NULL,
    PRIMARY KEY (correlation_id, ordinal)
);

CREATE TRIGGER trg_req_bulk_lookup_queue
AFTER INSERT ON req_bulk_lookup
BEGIN
    INSERT INTO host_queue (correlation_id, op_name)
    VALUES (NEW.correlation_id, 'bulkLookup');
END;
