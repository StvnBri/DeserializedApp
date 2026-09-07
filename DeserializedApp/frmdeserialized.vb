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

    Private Sub frmdeserialized_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        Get_MongDB_Credentials()
        Get_Source_Target()

    End Sub

    Public Sub Get_MongDB_Credentials()

        objconnectionautohrdw.Open()
        SQLCommand = New Data.SqlClient.SqlCommand("sproc_get_arcusair_uat_credentials", objconnectionautohrdw)
        SQLCommand.CommandType = CommandType.StoredProcedure
        SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection)

        If SQLReader.Read Then
            MongoDBConnectionString = SQLReader("AAConnectionString")
        Else
            MsgBox("Credentials Not Found")
            End
        End If
        objconnectionautohrdw.Close()

    End Sub

    Public Sub Get_Source_Target()

        Try

            objconnectionautohrdwLoop.Open()
            'SQLCommandLoop = New Data.SqlClient.SqlCommand("sproc_get_ArcusAir_Reference_Target_Reference", objconnectionautohrdwLoop) '
            'SQLCommandLoop = New Data.SqlClient.SqlCommand("sproc_aa_data_import_reference_data", objconnectionautohrdwLoop)
            SQLCommandLoop = New Data.SqlClient.SqlCommand("sproc_aa_data_import_reference_data_secondary", objconnectionautohrdwLoop)
            SQLCommandLoop.CommandType = CommandType.StoredProcedure
            SQLReaderLoop = SQLCommandLoop.ExecuteReader(Data.CommandBehavior.CloseConnection)


            Do While SQLReaderLoop.Read

                SourceDocument = SQLReaderLoop("Source_Collection")
                TargetTable = SQLReaderLoop("Target_Table")
                Clear_Destination(TargetTable)
                Extract_Data_From_MongoDB(MongoDBConnectionString, SourceDocument, TargetTable)

            Loop

            objconnectionautohrdwLoop.Close()

            End

        Catch ex As Exception

            objconnectionautohrdwLoop.Close()
            StartLog(SourceDocument, TargetTable & vbCrLf & ex.Message, 0)
            End
        End Try
    End Sub

    Public Sub Clear_Destination(ReferenceTbl As String)

        querystring = "Delete From " & ReferenceTbl & "  where '" & ReferenceTbl & "' in (Select Target_Table From Lst_Collection_Table_Reference where  Active = 1)"
        objconnectionautohrdw.Open()
        SQLCommand = New Data.SqlClient.SqlCommand(querystring, objconnectionautohrdw)
        SQLCommand.CommandType = CommandType.Text
        SQLCommand.ExecuteNonQuery()
        objconnectionautohrdw.Close()

    End Sub

    Public Sub Extract_Data_From_MongoDB(mongodbstr As String, SDocument As String, TTable As String)

        Dim lcnt As Integer
        Dim vcnt As Integer
        Dim dt As New DataTable
        Dim tempstr As String = ""
        Dim dr As DataRow
        Dim span As TimeSpan = TimeSpan.FromHours(2)

        Dim mongo As MongoClient = New MongoClient(mongodbstr)
        mongo.Settings.SocketTimeout = span

        Dim db = mongo.GetDatabase("arcusairdb")
        Dim collection = db.GetCollection(Of BsonDocument)(SDocument)
        Dim q = New BsonDocument()
        Dim f = Builders(Of BsonDocument).Projection.Exclude("resulttext")
        'Dim list = collection.Find(q).ToList()
        Dim list = collection.Find(q).Project(f).ToList




        StartLog(SDocument, TTable, list.Count)

        Do Until lcnt = list.Count

            dt.Rows.Add()
            vcnt = 0

            Do Until vcnt = list.Item(lcnt).Values.Count

                Try

                    'txtdata.Text = txtdata.Text & vbCrLf & list.Item(lcnt).ElementAt(vcnt).Name.ToString & "=" & list.Item(lcnt).Values(vcnt).ToString
                    vcnt = vcnt + 1
                    If list.Item(lcnt).ElementAt(vcnt - 1).Name.ToString.Contains("__v") = False Then
                        dt.Columns.Add(list.Item(lcnt).ElementAt(vcnt - 1).Name.ToString, GetType(String))
                        dt.Rows(0)(list.Item(lcnt).ElementAt(vcnt - 1).Name.ToString) = list.Item(lcnt).Values(vcnt - 1).ToString.Replace("[]", 0)
                    Else
                        ' StartLog(SDocument, TTable & vbCrLf & list.Item(lcnt).ElementAt(vcnt - 1).Name.ToString, 0)
                    End If


                Catch ex As Exception
                    StartLog(SDocument, TTable & vbCrLf & list.Item(lcnt).ElementAt(vcnt - 1).Name.ToString & vbCrLf & ex.Message & vbCrLf & "Extract_Data_From_MongoDB", 0)
                End Try

            Loop
            Process_Data_Transfer(TTable, dt)
            dt.Rows.Clear()
            dt.Columns.Clear()
            lcnt = lcnt + 1

        Loop

        EndLog(Lockid, vcnt)

    End Sub

    Public Sub Process_Data_Transfer(sourcetablename As String, sourcetable As DataTable)

        Dim columnstr As String

        Try

            objconnectionautohrdw.Open()
            Using SQLBulkCopy As SqlClient.SqlBulkCopy = New SqlClient.SqlBulkCopy(objconnectionautohrdw)

                For Each c As DataColumn In sourcetable.Columns
                    SQLBulkCopy.ColumnMappings.Add(c.ColumnName, c.ColumnName)
                    columnstr = columnstr & "," & c.ColumnName
                Next
                SQLBulkCopy.DestinationTableName = sourcetablename
                SQLBulkCopy.WriteToServer(sourcetable.CreateDataReader)
            End Using
            objconnectionautohrdw.Close()

        Catch ex As Exception
            objconnectionautohrdw.Close()
            StartLog(sourcetablename, columnstr & vbCrLf & ex.Message & vbCrLf & "Process_Data_Transfer", 0)

            'End
        End Try


    End Sub

    Public Function StartLog(Sdocument As String, TTable As String, SDocCount As Integer)

        Try

            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_save_logs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.Add("@Reference", SqlDbType.NVarChar, 1000, "@Reference")
            SQLCommand.Parameters("@Reference").Value = Sdocument
            SQLCommand.Parameters.Add("@Destination", SqlDbType.NVarChar, 4000, "@Destination")
            SQLCommand.Parameters("@Destination").Value = TTable
            SQLCommand.Parameters.Add("@ReferenceDocumentCount", SqlDbType.Int, 4, "@ReferenceDocumentCount")
            SQLCommand.Parameters("@ReferenceDocumentCount").Value = SDocCount
            SQLReader = SQLCommand.ExecuteReader(Data.CommandBehavior.CloseConnection)

            If SQLReader.Read Then
                Lockid = SQLReader("LockID")
            End If

            objconnectionautohrdw.Close()

        Catch ex As Exception
            objconnectionautohrdw.Close()
        End Try

    End Function

    Public Sub EndLog(lckid As Integer, DescCount As Integer)

        Try
            objconnectionautohrdw.Open()
            SQLCommand = New Data.SqlClient.SqlCommand("sproc_save_endlogs", objconnectionautohrdw)
            SQLCommand.CommandType = CommandType.StoredProcedure
            SQLCommand.Parameters.Add("@DestinationRowsCount", SqlDbType.Int, 4, "@DestinationRowsCount")
            SQLCommand.Parameters("@DestinationRowsCount").Value = lckid
            SQLCommand.Parameters.Add("@LockID", SqlDbType.Int, 4, "@LockID")
            SQLCommand.Parameters("@LockID").Value = DescCount
            SQLCommand.ExecuteNonQuery
            objconnectionautohrdw.Close()
        Catch ex As Exception
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


#End Region
End Class