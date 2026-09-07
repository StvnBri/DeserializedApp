Imports MongoDB.Bson
Imports MongoDB.Driver

Imports System.Data

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
    ' Public TEMPSTR As String

    Private Sub frmdeserialized_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Get_MongDB_Credentials()
        Get_Source_Target()
        End
    End Sub

    Public Sub Get_MongDB_Credentials()

        objconnectionautohrdw.Open() ' Open the db connection
        SQLCommand = New Data.SqlClient.SqlCommand("sproc_get_arcusair_uat_credentials", objconnectionautohrdw) ' Tells execute this stored procedure.
        SQLCommand.CommandType = CommandType.StoredProcedure ' specify that this is stored proc type and not a raw sql qeuery.
        SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection) ' finally exection of stored proc.

        ' Validation if the table has a row then grab the value and return it if empty it will return an error.
        If SQLReader.Read Then
            MongoDBConnectionString = SQLReader("AAConnectionString") ' True
        Else
            MsgBox("Credentials Not Found") ' False
            End
        End If
        objconnectionautohrdw.Close() ' close the db connection

    End Sub

    Public Sub Get_Source_Target()

        Try

            objconnectionautohrdwLoop.Open() ' open the db connection
            SQLCommandLoop = New Data.SqlClient.SqlCommand("sproc_get_ArcusAir_Reference_Target_Reference", objconnectionautohrdwLoop) ' Tells execute this stored proc
            SQLCommandLoop.CommandType = CommandType.StoredProcedure ' specify that this is stored proc type and not a raw sql qeuery.
            SQLReaderLoop = SQLCommandLoop.ExecuteReader(Data.CommandBehavior.CloseConnection) ' finally exection of stored proc.

            ' Validation: If a row exists → run the loop
            ' If no more rows → exit the loop
            Do While SQLReaderLoop.Read ' Starts a loop that iterates through each row returned by the stored procedure.
                ' .Read returns True if there is another row to read; otherwise, it ends the loop.

                SourceDocument = SQLReaderLoop("Source_Document") ' where it came from/Source
                TargetTable = SQLReaderLoop("Target_Table") ' where to write it/ Destination


                ' --- Start of Extraction ---
                Clear_Destination(TargetTable) ' Delete the existing data. Before inserting new.

                Extract_Data_From_MongoDB(MongoDBConnectionString, SourceDocument, TargetTable)
                ' --- End of Extraction & Loading (SQLBulkCopy happens inside Extract_Data_From_MongoDB) ---

            Loop

            objconnectionautohrdwLoop.Close()

            End

        Catch ex As Exception

            objconnectionautohrdwLoop.Close()
            'MsgBox(ex.Message)
            StartLog(SourceDocument, TargetTable & vbCrLf & ex.Message, 0)
            End
        End Try
    End Sub

    Public Sub Clear_Destination(ReferenceTbl As String)

        querystring = "Delete From " & ReferenceTbl & "  where '" & ReferenceTbl & "' in (Select Target_Table From Lst_Collection_Table_Reference where  Active = 1)"
        objconnectionautohrdw.Open()
        SQLCommand = New Data.SqlClient.SqlCommand(querystring, objconnectionautohrdw)
        SQLCommand.CommandType = CommandType.Text ' Code when raw SQL
        SQLCommand.ExecuteNonQuery() ' ExecuteNonQuery is used because DELETE does not return data, only affects rows.
        objconnectionautohrdw.Close() ' Closes the database connection after execution.

    End Sub

    Public Sub Extract_Data_From_MongoDB(mongodbstr As String, SDocument As String, TTable As String)

        Dim lcnt As Integer ' Row counter
        Dim vcnt As Integer ' Column/Value Counter

        Dim dtFull As New DataTable 'dtFull renamed for easy to distiguish from previous one. holds all extracted data before SQL insert : *** Temporary Variable

        Dim tempstr As String = ""
        Dim dr As DataRow ' adding new rows to dtFull/ represents a single row in dtFull

        ' --- Start of Extraction ---
        'Connect to mongo db
        Dim mongo As MongoClient = New MongoClient(mongodbstr)
        Dim db = mongo.GetDatabase("arcusairdb") ' arcusairdb mongodb name
        Dim collection = db.GetCollection(Of BsonDocument)(SDocument)
        Dim q = New BsonDocument() ' Create an empty query. An empty query means get all documents.
        Dim list = collection.Find(q).ToList() ' Retrieves all documents from the collection and stores them temporarily in memory.
        ' --- End of Extraction ---

        StartLog(SDocument, TTable, list.Count) ' Logs the process start and records how many documents were found.

        ' --- Start of Transformation ---
        ' Convert MongoDB documents to DataTable
        If list.Count > 0 Then
            For Each element As BsonElement In list.Item(0).Elements 'check if the column is already existing

                If Not dtFull.Columns.Contains(element.Name.ToString) Then ' check key/value pair

                    dtFull.Columns.Add(element.Name.ToString, GetType(String)) ' The data type is set to String.
                End If
            Next
        End If

        ' Outer Loop for looping for rows.
        Do Until lcnt = list.Count

            ' Creates a new row in the DataTable
            dr = dtFull.NewRow()
            dtFull.Rows.Add(dr) 'adding rows

            vcnt = 0

            ' Inner loop looping for fields/Columns
            Do Until vcnt = list.Item(lcnt).Values.Count

                Try
                    Dim columnName As String = list.Item(lcnt).ElementAt(vcnt).Name.ToString
                    Dim columnValue As String = list.Item(lcnt).Values(vcnt).ToString.Replace("[]", "0")

                    If columnName.Contains("__v") = False Then 'Ignores MongoDB’s internal __v field

                        ' If a Then column doesn't exist yet, create it. Column Type Is String
                        If Not dtFull.Columns.Contains(columnName) Then
                            dtFull.Columns.Add(columnName, GetType(String))
                        End If

                        'Populate the column for the current row dr. Saves the field value into the correct column of the current row
                        dr(columnName) = columnValue

                    End If
                    vcnt = vcnt + 1 ' Move to next field. Move to the new column.

                Catch ex As Exception
                    StartLog(SDocument, TTable & vbCrLf & ex.Message & vbCrLf & "Extract_Data_From_MongoDB - Inner Loop Error", 0)
                End Try
            Loop

            lcnt = lcnt + 1 ' Move to next document/row

            If lcnt = 360000 Then 'Limit 300,000 rows then exit to loop and proceed Process_Data_Transfer. Prevents memory overload
                Exit Do
            End If
        Loop
        ' --- End of Transformation ---

        ' --- Start of Loading ---
        If dtFull.Rows.Count > 0 Then
            Process_Data_Transfer(TTable, dtFull) ' Transfers collected data to the target SQL table
        End If
        ' --- End of Loading ---

        EndLog(Lockid, dtFull.Rows.Count)

    End Sub

    ' SQL Loading
    Public Sub Process_Data_Transfer(sourcetablename As String, sourcetable As DataTable)

        Dim columnstr As String 'Used to collect column names for error logging.

        Try
            ' Ensures the database connection starts clean.
            If objconnectionautohrdw.State = ConnectionState.Open Then
                objconnectionautohrdw.Close()
            End If

            objconnectionautohrdw.Open() ' Open the connection

            ' Creation of Object 'SQLBulkCopy'
            Using SQLBulkCopy As SqlClient.SqlBulkCopy = New SqlClient.SqlBulkCopy(objconnectionautohrdw)

                SQLBulkCopy.DestinationTableName = sourcetablename 'Specifies which SQL Server table will receive the data

                'SQLBulkCopy.BulkCopyTimeout = 600 ' 10 minutes (adjust as needed)


                For Each c As DataColumn In sourcetable.Columns

                    SQLBulkCopy.ColumnMappings.Add(c.ColumnName, c.ColumnName) ' it matches the field and columns.
                    columnstr = columnstr & "," & c.ColumnName ' Concatenates all column names into a single comma-separated string. Used later for error logging in case something fails
                Next

                ' --- Loading ---
                SQLBulkCopy.WriteToServer(sourcetable) ' write of the ENTIRE DataTable
                ' --- End of Loading ---
            End Using

            objconnectionautohrdw.Close() ' Close the connection after the process

        Catch ex As Exception
            If objconnectionautohrdw.State = ConnectionState.Open Then
                objconnectionautohrdw.Close()
            End If
            ' Log the error
            StartLog(sourcetablename, columnstr & vbCrLf & ex.Message & vbCrLf & "Process_Data_Transfer Error", 0)
        End Try

    End Sub

    Public Function StartLog(Sdocument As String, TTable As String, SDocCount As Integer)
        Try
            If objconnectionautohrdw.State = ConnectionState.Open Then
                objconnectionautohrdw.Close()
            End If

            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_save_logs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure

            ' ===PARAMETERS====
            SQLCommand.Parameters.Add("@Reference", SqlDbType.NVarChar, 1000, "@Reference")
            SQLCommand.Parameters("@Reference").Value = Sdocument

            SQLCommand.Parameters.Add("@Destination", SqlDbType.NVarChar, 4000, "@Destination")
            SQLCommand.Parameters("@Destination").Value = TTable

            SQLCommand.Parameters.Add("@ReferenceDocumentCount", SqlDbType.Int, 4, "@ReferenceDocumentCount")
            SQLCommand.Parameters("@ReferenceDocumentCount").Value = SDocCount

            SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection)

            'If SQLReader.Read Then
            '    Lockid = SQLReader("LockID")
            'End If

            objconnectionautohrdw.Close()

        Catch ex As Exception
            'MsgBox(ex.Message)
            objconnectionautohrdw.Close()
        End Try

    End Function

    Public Sub EndLog(lckid As Integer, DescCount As Integer)

        Exit Sub

        Try
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_save_endlogs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.Add("@DestinationRowsCount", SqlDbType.Int, 4, "@DestinationRowsCount")
            SQLCommand.Parameters("@DestinationRowsCount").Value = lckid
            SQLCommand.Parameters.Add("@LockID", SqlDbType.Int, 4, "@LockID")
            SQLCommand.Parameters("@LockID").Value = DescCount
            SQLCommand.ExecuteNonQuery()
            objconnectionautohrdw.Close()
        Catch ex As Exception
            'MsgBox(ex.Message)
            objconnectionautohrdw.Close()
        End Try
    End Sub


#Region "Test"


    Private Sub btndeserialized_Click(sender As Object, e As EventArgs) Handles btndeserialized.Click

        connect_mongo()

        'Dim mgdbresult = Newtonsoft.Json.JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(txtdata.Text)
        'Dim keys As String
        'Dim i As Integer = 0
        'Dim strlength As Integer

        'For Each entry As KeyValuePair(Of String, Object) In mgdbresult

        '    Try
        '        keys = keys & vbCrLf & entry.Key & " = " & entry.Value

        '    Catch ex As Exception
        '        Try

        '            keys = keys & vbCrLf & entry.Key & " = " & Check_Child_Values(entry.Value.ToString)

        '        Catch ex2 As Exception
        '            strlength = entry.Value.ToString.Length - 3
        '            keys = keys & vbCrLf & entry.Key & " = " '& entry.Value.ToString

        '        End Try


        '    End Try

        'Next

        'TextBox1.Text = keys

    End Sub
    Public Function Check_Child_Values(jsonsource As String)

        Dim mgdbchilddata = Newtonsoft.Json.JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(jsonsource)
        Dim childkeys As String
        Dim childvalues As String

        For Each childentry As KeyValuePair(Of String, Object) In mgdbchilddata
            childkeys = childentry.Value
        Next

        Return childkeys

    End Function
    Public Sub connect_mongo()

        Dim mongo As MongoClient = New MongoClient("mongodb://aaproject:temp%40123@10.16.250.156:45431/?authSource=arcusairdb") '("mongodb://localhost:27017") '("mongodb://DevTest:P%40ssw0rd@10.16.253.91:45431/?authSource=arcusairdb") '' 

        Dim db = mongo.GetDatabase("arcusairdb")
        Dim collection = db.GetCollection(Of BsonDocument)("inventorystores")
        Dim q = New BsonDocument()



        Dim startDate As DateTime = New DateTime(Now.Year, Now.Month, Now.Day)
        Dim endDate As DateTime = New DateTime(2023, 12, 31)

        ' Dim f = Builders(Of BsonDocument).Filter.Eq(Of Object)("_id", ObjectId.Parse("63c1242e8c568001880176d9"))
        'Dim f = Builders(Of BsonDocument).Filter.Eq(Of String)("grnnumber", "GR00000041")
        ' Dim f = Builders(Of BsonDocument).Filter.Eq(Of Date)("createdat", New DateTime(2023, 1, 13))
        ' Dim f = Builders(Of BsonDocument).Filter.And(Builders(Of BsonDocument).Filter.Gte("createdat", startDate), Builders(Of BsonDocument).Filter.Lte("createdat", endDate))
        Dim f = Builders(Of BsonDocument).Filter.And(Builders(Of BsonDocument).Filter.Gte(Of Date)("createdat", startDate))


        Dim list = collection.Find(q).ToList
        Dim lcnt As Integer
        Dim vcnt As Integer
        Dim dt As New DataTable
        Dim tempstr As String = ""
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
        ' MsgBox(dt.Columns.Count)
        End
        MsgBox("Connected")

    End Sub
    Public Sub Delete_GR_Current_day()
        '

        objconnectionautohrdw.Open()
        SQLCommand = New Data.SqlClient.SqlCommand("sproc_delete_gr_current_day", objconnectionautohrdw)
        SQLCommand.CommandType = CommandType.StoredProcedure
        SQLCommand.ExecuteNonQuery()
        objconnectionautohrdw.Close()


    End Sub
    Public Sub BulkUploadData(sourcetable As DataTable)

        Dim columnstr As String
        DestinationTable = "Lst_Inventory_Stores" '"Tbl_GoodsReceives" '"Tbl_PatientOrders"

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

    Private Sub TextBox1_TextChanged(sender As Object, e As EventArgs) Handles TextBox1.TextChanged

    End Sub


#End Region
End Class