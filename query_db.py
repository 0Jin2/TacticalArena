# -*- coding: utf-8 -*-
import sqlite3
import os

db_path = r'D:/GameProject/Backup/TacticalArena55/Library/SourceAssetDB'

# Try reading as SQLite first
try:
    conn = sqlite3.connect(db_path)
    cur = conn.cursor()
    cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
    tables = cur.fetchall()
    print('Tables:', [t[0] for t in tables])
    conn.close()
except Exception as e:
    print('SQLite error (may be Unity binary format):', e)
    # Try reading first bytes to identify format
    with open(db_path, 'rb') as f:
        header = f.read(16)
        print('File header (hex):', header.hex())
        print('File header (ascii):', header[:8])
