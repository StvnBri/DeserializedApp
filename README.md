# DeserializedApp

A full-refresh ETL (Extract, Transform, Load) utility built in VB.NET that migrates data from MongoDB to Microsoft SQL Server. Designed to synchronize reference and transactional data from an ArcusAir/IQVIA HIS MongoDB source into a SQL Server data warehouse.

## Overview

This tool performs a complete data refresh cycle — clearing destination tables, extracting documents from MongoDB collections, flattening nested/array structures into relational child tables, and bulk-loading the results into SQL Server.

## Key Features

- **MongoDB to SQL Server Migration** — Connects to MongoDB collections and transfers documents into corresponding SQL Server tables.
- **Recursive Nested Document Handling** — Automatically detects and processes nested arrays/objects within MongoDB documents, creating and populating parent-child detail tables as needed.
- **Dynamic Schema Mapping** — Builds SQL-compatible DataTable columns on the fly based on the structure of incoming BSON documents.
- **Batch Processing** — Uses configurable batch sizes (default 5,000 records) with SqlBulkCopy for efficient large-volume inserts.
- **Referential Integrity Management** — Temporarily disables and re-enables table constraints during clear/reload operations to maintain data consistency.
- **Centralized Logging** — Logs each processing step (document counts, errors, runtime) to SQL Server via stored procedures for audit and troubleshooting.
- **Credential Management** — Retrieves MongoDB connection strings securely from a SQL Server-stored configuration rather than hardcoding them.

## Core Components

| Component | Description |
|---|---|
| `Get_MongDB_Credentials` | Retrieves the MongoDB connection string from SQL Server |
| `Get_Source_Target` | Orchestrates the refresh process across all configured source/target table pairs |
| `Clear_Destination` / `Clear_All_Detail_Tables` | Clears existing data before reload, including recursive child table cleanup |
| `Extract_Data_From_MongoDB` | Pulls and transforms documents from a MongoDB collection into a staging DataTable |
| `Process_Data_Transfer` | Bulk-inserts staged data into the destination SQL Server table |
| `Process_Array_Migration` / `Recursive_Function` | Recursively processes nested arrays/objects into normalized child tables |
| `StartLog` | Records processing activity and errors to a persistent SQL Server log |

## Tech Stack

- VB.NET (WinForms)
- MongoDB.Driver / MongoDB.Bson
- ADO.NET (SqlClient) with SqlBulkCopy
- SQL Server (stored procedures for credentials, logging, and metadata)

## Notes

- Requires a valid connection string configured under `connectionStringDW` in application settings.
- Source-to-target table mappings are managed via SQL Server metadata (`sproc_get_ArcusAir_Reference_Target_Reference`).

1. The initial version of this system was built in 09/22/2025
2. version built 09/23/2025
3. version built 11/28/2025
4. version built 12/30/2025
5. version built 08/08/2026
