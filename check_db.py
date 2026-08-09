import sqlite3

conn = sqlite3.connect('Gadgetdb.db')

print("=== ALL TABLES ===")
tables = conn.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall()
for t in tables:
    print(t[0])

print()
print("=== users table (with role_id) ===")
for r in conn.execute("SELECT user_id, name, role_id FROM users").fetchall():
    print(r)

print()
print("=== Check what table has roles ===")
for t in tables:
    tname = t[0]
    cols = conn.execute(f"PRAGMA table_info({tname})").fetchall()
    col_names = [c[1] for c in cols]
    if 'role_id' in col_names or 'role_name' in col_names or 'name' in col_names:
        print(f"  {tname}: {col_names}")

print()
print("=== orders row 5 (the bad one with empty total) ===")
for r in conn.execute("SELECT * FROM orders WHERE order_id=5").fetchall():
    print(r)
