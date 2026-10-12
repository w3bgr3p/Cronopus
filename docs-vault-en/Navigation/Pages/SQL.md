# SQL

SQL is a separate panel for SQLite files and PostgreSQL connections.
Choose the database type in the header. The page keeps its existing
`/?page=sqlite` address and does not change the main z3nDash database.

## SQLite

- open a database file;
- list its tables;
- run a query;
- change a value;
- delete a row.

SQLite works on the selected file passed in the viewer API request.
Cell editing and row deletion are available only for SQLite.

## PostgreSQL

Select `PostgreSQL`, enter a connection string and press `Connect`.
The panel lists accessible tables and views with their schema names.
Selecting a table queries its first 1000 rows; use the SQL field to run your
own query. `SELECT` and `WITH` queries returning a read-only result are supported.

Queries run inside a read-only transaction. Results are capped at 10,000 rows;
the panel reports truncation. Use `WHERE` or `LIMIT` to narrow large queries.
Connection timeout is 10 seconds, command timeout is 30 seconds, and the
overall request timeout is 40 seconds.

PostgreSQL data editing and saved connection profiles are not supported.
The connection string stays on the current page and is cleared when switching
sources or reloading. AI context includes the database name, SQL and displayed
data; it excludes the connection string.

## API

- `POST /sqlite-viewer/tables`
- `POST /sqlite-viewer/query`
- `POST /sqlite-viewer/update`
- `POST /sqlite-viewer/delete`
- `POST /postgres-viewer/tables`
- `POST /postgres-viewer/query`
