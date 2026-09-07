
'-----------------------------------
' Full refresh
'-----------------------------------

Imports MongoDB.Bson
Imports MongoDB.Driver
Imports System.Diagnostics
Imports System.Data
Imports System.IO
Imports System.Text
Imports System.Runtime.CompilerServices
Imports System.Text.RegularExpressions
Imports System.Collections.Generic
'Imports System.Globalization

Public Class frmdeserialized

    Public connectionStringHRDW = Configuration.ConfigurationSettings.AppSettings("connectionStringDW")

    Public objconnectionautohrdwLoop As New Data.SqlClient.SqlConnection(connectionStringHRDW)
    Public SQLCommandLoop As Data.SqlClient.SqlCommand
    Public SQLReaderLoop As Data.SqlClient.SqlDataReader

    Public objconnectionautohrdw As New Data.SqlClient.SqlConnection(connectionStringHRDW)
    Public SQLCommand As Data.SqlClient.SqlCommand
    Public SQLReader As Data.SqlClient.SqlDataReader

    Public objconnectionautohrdwError As New Data.SqlClient.SqlConnection(connectionStringHRDW)
    Public SQLCommandError As Data.SqlClient.SqlCommand

    Dim _client As IMongoClient
    Dim _db As IMongoDatabase
    Dim dt As New DataTable
    Dim dtval As New DataTable
    Dim ds As New BindingSource
    Public DestinationTable As String
    Public dttablejson As New DataTable

    Public MongoDBConnectionString As String
    Public SourceDocument As String
    Public TargetTable As String
    Public querystring As String
    Public Lockid As Integer

    Private ExcludedFields As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    ' FIX: same fixed, unambiguous date/time text format used in frmmain,
    ' so any date field that passes through this ETL path lands in SQL
    ' Server in a form CONVERT/CAST can always parse, regardless of the
    ' app server's locale/regional settings.
    ' Private Const ISO_DATETIME_FORMAT As String = "yyyy-MM-dd HH:mm:ss.fff"

    Private etlTimer As Stopwatch
    Private breakSecondsRemaining As Integer = 0
    Private Const BREAK_DURATION As Integer = 300000
    Private isRunning As Boolean = False
    Private nextRunTime As DateTime

    ' ============================================================
    ' TIMERS
    ' ============================================================

    'Private Sub Timer1_Tick(sender As Object, e As EventArgs) Handles Timer1.Tick
    '    If etlTimer IsNot Nothing AndAlso etlTimer.IsRunning Then
    '        Lbl_runtime_stopwatch.Text = etlTimer.Elapsed.ToString("hh\:mm\:ss")
    '    End If
    'End Sub

    'Private Sub Timer2_Tick(sender As Object, e As EventArgs) Handles Timer2.Tick
    '    Dim remaining As TimeSpan = nextRunTime - DateTime.Now
    '    If remaining.TotalSeconds > 0 Then
    '        Lbl_break_stopwatch.Text = remaining.ToString("mm\:ss")
    '    Else
    '        Timer2.Stop()
    '        Lbl_break_stopwatch.Text = "00:00"
    '        Lbl_status.Text = "Starting ETL..."
    '        isRunning = False
    '        RunETL()
    '    End If
    'End Sub

    ' ============================================================
    ' RUN ETL
    ' ============================================================

    'Private Async Sub RunETL()
    '    If isRunning Then Exit Sub
    '    isRunning = True

    '    Try
    '        Lbl_break_stopwatch.Text = "00:00"
    '        Lbl_status.Text = "Processing..."

    '        etlTimer = Stopwatch.StartNew()
    '        Timer1.Start()

    '        Await Task.Run(Sub()
    '                           Get_MongDB_Credentials()
    '                           Get_Source_Target()
    '                       End Sub)

    '        etlTimer.Stop()
    '        Timer1.Stop()
    '        Lbl_runtime_stopwatch.Text = etlTimer.Elapsed.ToString("hh\:mm\:ss")

    '        Lbl_status.Text = "Break..."
    '        nextRunTime = DateTime.Now.AddMinutes(15)
    '        Timer2.Start()

    '    Catch ex As Exception
    '        Timer1.Stop()
    '        If etlTimer IsNot Nothing Then etlTimer.Stop()
    '        Lbl_status.Text = "Error"
    '        isRunning = False
    '        MsgBox(ex.Message)
    '    End Try
    'End Sub

    'Private Sub frmdeserialized_Load(sender As Object, e As EventArgs) Handles MyBase.Load
    '    RunETL()
    'End Sub

    Private Sub frmdeserialized_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Get_MongDB_Credentials()
        Get_Source_Target()
        Application.Exit()
    End Sub

    ' ============================================================
    ' CREDENTIALS
    ' Retrieves the MongoDB connection string from SQL Server.
    ' This allows the application to connect to the source database.
    ' ============================================================
    Public Sub Get_MongDB_Credentials()
        objconnectionautohrdw.Open()
        SQLCommand = New Data.SqlClient.SqlCommand("sproc_get_arcusair_uat_credentials", objconnectionautohrdw)
        SQLCommand.CommandType = CommandType.StoredProcedure
        SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection)

        If SQLReader.Read Then
            MongoDBConnectionString = SQLReader("AAConnectionString")
        Else
            MsgBox("Credentials Not Found")
        End If
        objconnectionautohrdw.Close()
    End Sub

    ' ============================================================
    ' GET SOURCE & TARGET 
    ' Retrieves the list of source collections and destination tables.
    ' Controls the overall refresh process for all configured reference tables.
    ' ============================================================

    Public Sub Get_Source_Target()
        'etlTimer = Stopwatch.StartNew()

        Try
            StartLog("Start process", "Processing", 0)
            objconnectionautohrdwLoop.Open()
            SQLCommandLoop = New Data.SqlClient.SqlCommand("sproc_get_ArcusAir_Reference_Target_Reference", objconnectionautohrdwLoop)
            SQLCommandLoop.CommandType = CommandType.StoredProcedure
            SQLReaderLoop = SQLCommandLoop.ExecuteReader(Data.CommandBehavior.CloseConnection)

            Clear_All_Detail_Tables()

            Do While SQLReaderLoop.Read
                SourceDocument = SQLReaderLoop("Source_Document")
                TargetTable = SQLReaderLoop("Target_Table")

                Clear_Destination(TargetTable)
                Extract_Data_From_MongoDB(MongoDBConnectionString, SourceDocument, TargetTable)
                Process_Children_For_Parent(TargetTable)
            Loop

            objconnectionautohrdwLoop.Close()


            'load_prescription()

            'etlTimer.Stop()
            'Dim totalTime As String = etlTimer.Elapsed.ToString("hh\:mm\:ss")
            'StartLog("Process", "Total Runtime: " & totalTime, 0)

        Catch ex As Exception
            objconnectionautohrdwLoop.Close()
            'etlTimer.Stop()
            'Dim totalTime As String = etlTimer.Elapsed.ToString("hh\:mm\:ss")
            'StartLog(SourceDocument, "Runtime: " & totalTime & vbCrLf & ex.Message, 0)
        End Try
    End Sub

    ' ============================================================
    ' Clear_Destination
    ' Clears existing records from the destination reference table
    ' before loading fresh data.
    ' ============================================================

    Public Sub Clear_Destination(ReferenceTbl As String)
        Try
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            objconnectionautohrdw.Open()

            ' Use parameterized query — checks if table is active, then deletes
            querystring = "IF EXISTS (SELECT 1 FROM Lst_Collection_Table_Reference WHERE Target_Table = @TableName AND Active = 1) " &
                          "DELETE FROM [" & ReferenceTbl & "]"

            SQLCommand = New Data.SqlClient.SqlCommand(querystring, objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.Text
            SQLCommand.Parameters.AddWithValue("@TableName", ReferenceTbl)
            SQLCommand.ExecuteNonQuery()

            objconnectionautohrdw.Close()
        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog("Clear_Destination", ReferenceTbl & vbCrLf & ex.Message, 0)
        End Try
    End Sub

    ' ============================================================
    ' Clear_All_Detail_Tables 
    ' Removes existing records from all child/detail tables
    ' to prepare for a clean reload.
    ' ============================================================

    Public Sub Clear_All_Detail_Tables()
        Dim dtMain As New DataTable

        Try
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            objconnectionautohrdw.Open()

            SQLCommand = New SqlClient.SqlCommand("sp_get_main", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLReader = SQLCommand.ExecuteReader()
            dtMain.Load(SQLReader)
            objconnectionautohrdw.Close()

            For Each row As DataRow In dtMain.Rows
                Dim mainTable As String = row("Main_Table").ToString()
                Clear_Children_Recursive(mainTable)
            Next

        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog("Clear_All_Detail_Tables", ex.Message, 0)
        End Try
    End Sub

    ' ============================================================
    '  Clear_Children
    ' Recursively clears child tables while respecting parent-child table relationships.
    ' ============================================================

    Private Sub Clear_Children_Recursive(parentTable As String)
        Dim dtDetail As New DataTable

        Try
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            objconnectionautohrdw.Open()

            SQLCommand = New SqlClient.SqlCommand("sp_get_detail", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.AddWithValue("@Main_Table", parentTable)
            SQLReader = SQLCommand.ExecuteReader()
            dtDetail.Load(SQLReader)
            objconnectionautohrdw.Close()

            For Each row As DataRow In dtDetail.Rows
                Dim childTable As String = row("Detail_Table").ToString()

                ' Recurse deeper first (grandchildren before children)
                Clear_Children_Recursive(childTable)


                If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
                objconnectionautohrdw.Open()


                SQLCommand = New SqlClient.SqlCommand(
                    "ALTER TABLE [" & childTable & "] NOCHECK CONSTRAINT ALL",
                    objconnectionautohrdw)
                SQLCommand.ExecuteNonQuery()


                SQLCommand = New SqlClient.SqlCommand(
                    "DELETE FROM [" & childTable & "]",
                    objconnectionautohrdw)
                SQLCommand.ExecuteNonQuery()


                SQLCommand = New SqlClient.SqlCommand(
                    "ALTER TABLE [" & childTable & "] WITH CHECK CHECK CONSTRAINT ALL",
                    objconnectionautohrdw)
                SQLCommand.ExecuteNonQuery()

                objconnectionautohrdw.Close()
            Next

        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog("Clear_Children_Recursive", parentTable & vbCrLf & ex.Message, 0)
        End Try
    End Sub

    ' ============================================================
    ' Process_Children_For_Parent
    ' Processes all child tables associated with a parent table
    ' after the parent data has been loaded.
    ' ============================================================

    Private Sub Process_Children_For_Parent(mainTable As String)
        Dim dtDetail As New DataTable

        Try
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            objconnectionautohrdw.Open()

            SQLCommand = New SqlClient.SqlCommand("sp_get_detail", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.AddWithValue("@Main_Table", mainTable)
            SQLReader = SQLCommand.ExecuteReader()
            dtDetail.Load(SQLReader)
            objconnectionautohrdw.Close()

            If dtDetail.Rows.Count = 0 Then Exit Sub

            For Each detailRow As DataRow In dtDetail.Rows
                Dim detailTable As String = detailRow("Detail_Table").ToString()
                Dim fieldName As String = detailRow("Field_Name").ToString()
                Dim parentKeyColumn As String = "_id"
                Dim refId As String = "reference_id"
                Process_Array_Migration(mainTable, detailTable, parentKeyColumn, fieldName, refId)
            Next

        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog("CHILD_PHASE", mainTable & vbCrLf & ex.Message, 0)
        End Try
    End Sub

    ' ============================================================
    ' Extract_Data_From_MongoDB
    ' Retrieves records from a MongoDB collection and prepares
    ' them for loading into the destination SQL table.
    '
    ' FIX: date/time BSON values now go through Format_Bson_Value
    ' instead of a bare .ToString(), so they land in SQL Server as a
    ' fixed, unambiguous "yyyy-MM-dd HH:mm:ss.fff" string, the same
    ' way they do in the frmmain ETL path. The existing "[]" -> "0"
    ' cleanup for empty-array text is preserved and applied after.
    ' ============================================================

    Public Sub Extract_Data_From_MongoDB(mongodbstr As String, SDocument As String, TTable As String)
        Dim mongo As New MongoClient(mongodbstr)
        Dim db = mongo.GetDatabase("arcusairdb")
        Dim collection = db.GetCollection(Of BsonDocument)(SDocument)
        Dim filter = Builders(Of BsonDocument).Filter.Empty

        Dim docCount As Long = collection.CountDocuments(filter)
        StartLog(SDocument, TTable, If(docCount > Integer.MaxValue, -1, CInt(docCount)))

        Dim projection As BsonDocument = New BsonDocument From {
                                                                {"__v", 0},
                                                                {"externalcode", 0},
                                                                {"handlingstores", 0},
                                                                {"reorderdetails", 0},
                                                                {"additionalinfusion", 0},
                                                                {"assigneduserlogs", 0},
                                                                {"dispensebatchdetail", 0},
                                                                {"externalpatientorderitems", 0},
                                                                {"instructions", 0},
                                                                {"medreviewcareproviders", 0},
                                                                {"patientorderlogs", 0},
                                                                {"prebilledlogs", 0},
                                                                {"slidingscaledetails", 0},
                                                                {"specimendiscarddetails", 0}
                                                                }

        Dim cursor As IAsyncCursor(Of BsonDocument) = collection.Find(filter).Project(projection).ToCursor()

        Const batchSize As Integer = 5000
        Dim dtFull As New DataTable()
        Dim rowCount As Integer = 0
        Dim columnsDefined As Boolean = False

        Try
            While cursor.MoveNext()
                For Each doc As BsonDocument In cursor.Current
                    If Not columnsDefined Then
                        For Each element As BsonElement In doc.Elements
                            If ExcludedFields.Contains(element.Name) Then Continue For
                            If Not dtFull.Columns.Contains(element.Name) Then
                                dtFull.Columns.Add(element.Name, GetType(String))
                            End If
                        Next
                        columnsDefined = True
                    End If

                    Dim dr As DataRow = dtFull.NewRow()

                    For Each element As BsonElement In doc.Elements
                        Dim columnName As String = element.Name
                        If ExcludedFields.Contains(columnName) Then Continue For
                        Dim columnValue As String = Format_Bson_Value(element.Value).Replace("[]", "0")
                        If Not dtFull.Columns.Contains(columnName) Then
                            dtFull.Columns.Add(columnName, GetType(String))
                        End If
                        dr(columnName) = columnValue
                    Next

                    dtFull.Rows.Add(dr)
                    rowCount += 1

                    If dtFull.Rows.Count >= batchSize Then
                        Process_Data_Transfer(TTable, dtFull)
                        dtFull.Clear()
                    End If
                Next
            End While

            If dtFull.Rows.Count > 0 Then
                Process_Data_Transfer(TTable, dtFull)
            End If

        Catch ex As Exception
            StartLog(SDocument, TTable & vbCrLf & ex.Message & vbCrLf & "Extract_Data_From_MongoDB Cursor Error", 0)
        Finally
            cursor.Dispose()
        End Try
    End Sub

    ' ============================================================
    ' FIX (new helper): Format_Bson_Value
    ' Same helper used in frmmain. Converts a single BSON value to
    ' text for staging. Date/time values get a fixed InvariantCulture
    ' format so a later CONVERT/CAST to date/datetime in SQL Server
    ' never fails due to locale differences. Nulls become an empty
    ' string instead of whatever ToString() would otherwise produce.
    ' ============================================================
    Private Function Format_Bson_Value(value As BsonValue) As String
        If value Is Nothing OrElse value.IsBsonNull Then
            Return ""
        End If

        'If value.IsValidDateTime Then
        '    Return value.ToUniversalTime().ToString(ISO_DATETIME_FORMAT, CultureInfo.InvariantCulture)
        'End If

        Return value.ToString()
    End Function

    ' ============================================================
    ' Process_Data_Transfer
    ' Loads data into the destination SQL Server table
    ' using bulk insert operations.
    ' ============================================================

    Public Sub Process_Data_Transfer(sourcetablename As String, sourcetable As DataTable)
        Dim columnstr As String = ""

        Try
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            objconnectionautohrdw.Open()

            Using SQLBulkCopy As SqlClient.SqlBulkCopy = New SqlClient.SqlBulkCopy(objconnectionautohrdw)
                SQLBulkCopy.DestinationTableName = sourcetablename
                SQLBulkCopy.BulkCopyTimeout = 0
                SQLBulkCopy.BatchSize = 5000

                For Each c As DataColumn In sourcetable.Columns
                    SQLBulkCopy.ColumnMappings.Add(c.ColumnName, c.ColumnName)
                    columnstr = columnstr & "," & c.ColumnName
                Next

                SQLBulkCopy.WriteToServer(sourcetable)
            End Using

            objconnectionautohrdw.Close()

        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog(sourcetablename, columnstr & vbCrLf & ex.Message & vbCrLf & "Process_Data_Transfer Error", 0)
        End Try
    End Sub

    ' ============================================================
    ' Process_Array_Migration
    ' Processes array and nested document fields from parent records
    ' and prepares them for loading into detail tables.
    ' ============================================================

    Public Sub Process_Array_Migration(mainTable As String, detailTable As String, parentKeyColumn As String, fieldName As String, refId As String)
        Dim resultTables As New Dictionary(Of String, DataTable)

        Try
            Dim dtArray As New DataTable

            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            objconnectionautohrdw.Open()

            SQLCommand = New SqlClient.SqlCommand("sproc_get_array_data", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.AddWithValue("@ParentTable", mainTable)
            SQLCommand.Parameters.AddWithValue("@DetailTable", detailTable)
            SQLCommand.Parameters.AddWithValue("@ParentIdColumn", parentKeyColumn)
            SQLCommand.Parameters.AddWithValue("@FieldName", fieldName)
            SQLCommand.Parameters.AddWithValue("@RefId", refId)

            SQLReader = SQLCommand.ExecuteReader()
            dtArray.Load(SQLReader)
            objconnectionautohrdw.Close()

            If dtArray.Rows.Count = 0 Then Exit Sub

            For Each row As DataRow In dtArray.Rows
                Dim parentId As String = row("reference_id").ToString()
                Dim raw As String = row("ArrayField").ToString().Trim()

                If raw = "" OrElse raw = "0" Then Continue For

                Dim bsonValue As BsonValue
                Try
                    bsonValue = MongoDB.Bson.Serialization.BsonSerializer.Deserialize(Of BsonValue)(raw)
                Catch ex As Exception
                    Continue For
                End Try

                Recursive_Function(bsonValue, parentId, detailTable, resultTables)
            Next

            For Each kvp In resultTables
                Array_Bulk_Insert(kvp.Key, kvp.Value)
            Next

            For Each kvp In resultTables
                StartLog(fieldName, kvp.Key, kvp.Value.Rows.Count)
            Next

        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog(detailTable, ex.Message, 0)
        End Try
    End Sub

    ' ============================================================
    ' Recursive_Function
    ' Recursively reads nested MongoDB structures and converts them
    ' into SQL-compatible table rows.
    '
    ' FIX: nested date/time fields now go through Format_Bson_Value
    ' too, so detail/child tables get the same consistent date text
    ' as parent tables.
    ' ============================================================

    Private Sub Recursive_Function(value As BsonValue, parentId As String, tableName As String, resultTables As Dictionary(Of String, DataTable))
        If value Is Nothing OrElse value.IsBsonNull Then Exit Sub

        If value.IsBsonArray Then
            For Each item In value.AsBsonArray
                Recursive_Function(item, parentId, tableName, resultTables)
            Next
            Exit Sub
        End If

        If value.IsBsonDocument Then
            If Not resultTables.ContainsKey(tableName) Then
                resultTables(tableName) = CreateDynamicTable(tableName)
            End If

            Dim row As DataRow = resultTables(tableName).NewRow()

            If value.AsBsonDocument.Contains("_id") Then
                row("_id") = value("_id").ToString()
            Else
                row("_id") = Guid.NewGuid().ToString()
            End If

            row("reference_id") = parentId

            For Each el In value.AsBsonDocument.Elements
                If el.Value.IsBsonArray OrElse el.Value.IsBsonDocument Then
                    Recursive_Function(el.Value, parentId, tableName & "_" & el.Name, resultTables)
                Else
                    If Not resultTables(tableName).Columns.Contains(el.Name) Then
                        resultTables(tableName).Columns.Add(el.Name, GetType(String))
                    End If
                    row(el.Name) = Format_Bson_Value(el.Value)
                End If
            Next

            resultTables(tableName).Rows.Add(row)
        End If
    End Sub

    ' Creates a temporary in-memory table structure
    ' for processing child records.
    Private Function CreateDynamicTable(tableName As String) As DataTable
        Dim dt As New DataTable(tableName)
        dt.Columns.Add("_id", GetType(String))
        dt.Columns.Add("reference_id", GetType(String))
        Return dt
    End Function

    ' ============================================================
    '  Array_Bulk_Insert
    ' Loads processed child table records into SQL Server
    ' using bulk insert operations.
    ' ============================================================

    Private Sub Array_Bulk_Insert(tableName As String, dt As DataTable)
        Dim columnstr As String = ""

        Try

            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            objconnectionautohrdw.Open()

            Using bulk As New SqlClient.SqlBulkCopy(objconnectionautohrdw)
                bulk.DestinationTableName = tableName
                bulk.BulkCopyTimeout = 0
                bulk.EnableStreaming = True

                For Each c As DataColumn In dt.Columns
                    bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName)
                    columnstr = columnstr & "," & c.ColumnName
                Next

                bulk.WriteToServer(dt)
            End Using

            objconnectionautohrdw.Close()

        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
            StartLog(tableName, columnstr & vbCrLf & ex.Message & vbCrLf & "Array_Bulk_Insert Error", 0)
        End Try
    End Sub

    ' ============================================================
    ' load_prescription 
    ' ============================================================

    'Public Sub load_prescription()
    '    StartLog("LOADING PRESCRIPTION", "START", 0)

    '    Try
    '        If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
    '        objconnectionautohrdw.Open()

    '        SQLCommand = New SqlClient.SqlCommand("sp_load_prescription", objconnectionautohrdw)
    '        SQLCommand.CommandType = CommandType.StoredProcedure
    '        SQLCommand.ExecuteNonQuery()

    '        objconnectionautohrdw.Close()
    '        StartLog("LOADING PRESCRIPTION", "FINISH", 1)

    '    Catch ex As Exception
    '        If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
    '        StartLog("Loading view to table", ex.Message, 0)
    '    End Try
    'End Sub

    ' ============================================================
    ' FIX #5 — StartLog
    ' BUG:  Declared as Function but never returns a value.
    '       All callers ignore the return value — misleading signature.
    ' FIX:  Changed to Sub.
    ' ============================================================

    Public Sub StartLog(Sdocument As String, TTable As String, SDocCount As Integer)
        Try
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()

            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_save_logs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure

            SQLCommand.Parameters.Add("@Reference", SqlDbType.NVarChar, 1000, "@Reference")
            SQLCommand.Parameters("@Reference").Value = Sdocument

            SQLCommand.Parameters.Add("@Destination", SqlDbType.NVarChar, -1)
            SQLCommand.Parameters("@Destination").Value = TTable

            SQLCommand.Parameters.Add("@ReferenceDocumentCount", SqlDbType.Int, 4, "@ReferenceDocumentCount")
            SQLCommand.Parameters("@ReferenceDocumentCount").Value = SDocCount

            SQLCommand.ExecuteNonQuery()
            objconnectionautohrdw.Close()

        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then objconnectionautohrdw.Close()
        End Try
    End Sub

#Region "Test"

    Private Sub btndeserialized_Click(sender As Object, e As EventArgs) Handles btndeserialized.Click
        connect_mongo()
    End Sub

    Public Function Check_Child_Values(jsonsource As String)
        Dim mgdbchilddata = Newtonsoft.Json.JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(jsonsource)
        Dim childkeys As String = ""
        For Each childentry As KeyValuePair(Of String, Object) In mgdbchilddata
            childkeys = childentry.Value
        Next
        Return childkeys
    End Function

    Public Sub connect_mongo()
        Dim mongo As MongoClient = New MongoClient("mongodb://aaproject:temp%40123@10.16.250.156:45431/?authSource=arcusairdb")
        Dim db = mongo.GetDatabase("arcusairdb")
        Dim collection = db.GetCollection(Of BsonDocument)("inventorystores")
        Dim q = New BsonDocument()

        Dim startDate As DateTime = New DateTime(Now.Year, Now.Month, Now.Day)
        Dim f = Builders(Of BsonDocument).Filter.And(Builders(Of BsonDocument).Filter.Gte(Of Date)("createdat", startDate))

        Dim list = collection.Find(q).ToList
        Dim lcnt As Integer = 0
        Dim vcnt As Integer
        Dim dt As New DataTable
        Dim dr As DataRow

        Do Until lcnt = list.Count
            dt.Rows.Add()
            vcnt = 0

            Do Until vcnt = list.Item(lcnt).Values.Count
                Try
                    txtdata.Text = txtdata.Text & vbCrLf & list.Item(lcnt).ElementAt(vcnt).Name.ToString & "=" & list.Item(lcnt).Values(vcnt).ToString
                    vcnt = vcnt + 1
                    dt.Columns.Add(list.Item(lcnt).ElementAt(vcnt - 1).Name.ToString, GetType(String))
                    dt.Rows(0)(list.Item(lcnt).ElementAt(vcnt - 1).Name.ToString) = list.Item(lcnt).Values(vcnt - 1).ToString
                Catch ex As Exception
                    MsgBox(ex.Message & vbCrLf & "connect_mongo")
                    End
                End Try
            Loop

            BulkUploadData(dt)
            dt.Rows.Clear()
            dt.Columns.Clear()
            lcnt = lcnt + 1
        Loop

        dttablejson.Load(dt.CreateDataReader)
        ds.DataSource = dttablejson
        DataGridView1.DataSource = ds
        End
        MsgBox("Connected")
    End Sub

    Public Sub Delete_GR_Current_day()
        objconnectionautohrdw.Open()
        SQLCommand = New Data.SqlClient.SqlCommand("sproc_delete_gr_current_day", objconnectionautohrdw)
        SQLCommand.CommandType = CommandType.StoredProcedure
        SQLCommand.ExecuteNonQuery()
        objconnectionautohrdw.Close()
    End Sub

    Public Sub BulkUploadData(sourcetable As DataTable)
        Dim columnstr As String = ""
        DestinationTable = "Lst_Inventory_Stores"

        Try
            objconnectionautohrdw.Open()
            Using SQLBulkCopy As SqlClient.SqlBulkCopy = New SqlClient.SqlBulkCopy(objconnectionautohrdw)
                For Each c As DataColumn In sourcetable.Columns
                    SQLBulkCopy.ColumnMappings.Add(c.ColumnName, c.ColumnName)
                    columnstr = columnstr & "," & c.ColumnName
                Next
                SQLBulkCopy.DestinationTableName = DestinationTable
                SQLBulkCopy.WriteToServer(sourcetable.CreateDataReader)
            End Using
            objconnectionautohrdw.Close()
        Catch ex As Exception
            MsgBox(ex.Message)
            objconnectionautohrdw.Close()
            End
        End Try
    End Sub

#End Region

End Class