import sqlite3

conn = sqlite3.connect('Gadgetdb.db')

# Fix order 5 - total_amount stored as '' (empty string) - set it to 0
conn.execute("UPDATE orders SET total_amount = 0 WHERE order_id = 5 AND (total_amount = '' OR total_amount IS NULL)")
conn.commit()

print("Fixed rows:", conn.execute("SELECT changes()").fetchone()[0])
print("Order 5 now:", conn.execute("SELECT * FROM orders WHERE order_id = 5").fetchone())
conn.close()
