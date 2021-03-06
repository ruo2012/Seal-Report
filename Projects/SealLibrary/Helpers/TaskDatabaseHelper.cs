﻿//
// Copyright (c) Seal Report, Eric Pfirsch (sealreport@gmail.com), http://www.sealreport.org.
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License. http://www.apache.org/licenses/LICENSE-2.0..
//
using Seal.Model;
using System;
using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace Seal.Helpers
{
    public delegate string CustomGetTableCreateCommand(DataTable table);

    public delegate string CustomGetTableColumnNames(DataTable table);
    public delegate string CustomGetTableColumnName(DataColumn col);
    public delegate string CustomGetTableColumnType(DataColumn col);

    public delegate string CustomGetTableColumnValues(DataRow row, string dateTimeFormat);
    public delegate string CustomGetTableColumnValue(DataRow row, DataColumn col, string datetimeFormat);

    public delegate DataTable CustomLoadDataTable(string connectionString, string sql);
    public delegate DataTable CustomLoadDataTableFromExcel(string excelPath, string tabName = "");
    public delegate DataTable CustomLoadDataTableFromCSV(string csvPath, char? separator = null);

    public class TaskDatabaseHelper
    {
        //Config, may be overwritten
        public string ColumnCharType = "";
        public string ColumnNumericType = "";
        public string ColumnIntegerType = "";
        public string ColumnDateTimeType = "";
        public string InsertStartCommand = "";
        public string InsertEndCommand = "";
        public int ColumnCharLength = 0; //0= means auto size
        public int NoRowsCharLength = 50; //Char length taken if the table loaded as no row
        public int LoadBurstSize = 0; //0 = Load all records in one
        public string LoadSortColumn = ""; //Sort column used if LoadBurstSize is specified
        public bool UseDbDataAdapter = false;
        public int InsertBurstSize = 500;
        public string ExcelOdbcDriver = "Driver={{Microsoft Excel Driver (*.xls, *.xlsx, *.xlsm, *.xlsb)}};DBQ={0}";
        public Encoding DefaultEncoding = Encoding.Default;
        public bool TrimText = true;
        public bool RemoveCrLf = false;

        public bool DebugMode = false;
        public StringBuilder DebugLog = new StringBuilder();
        public int SelectTimeout = 0;

        public CustomGetTableCreateCommand MyGetTableCreateCommand = null;

        public CustomGetTableColumnNames MyGetTableColumnNames = null;
        public CustomGetTableColumnName MyGetTableColumnName = null;
        public CustomGetTableColumnType MyGetTableColumnType = null;

        public CustomGetTableColumnValues MyGetTableColumnValues = null;
        public CustomGetTableColumnValue MyGetTableColumnValue = null;

        public CustomLoadDataTable MyLoadDataTable = null;
        public CustomLoadDataTableFromExcel MyLoadDataTableFromExcel = null;
        public CustomLoadDataTableFromCSV MyLoadDataTableFromCSV = null;


        string _defaultColumnCharType = "";
        string _defaultColumnIntegerType = "";
        string _defaultColumnNumericType = "";
        string _defaultColumnDateTimeType = "";
        string _defaultInsertStartCommand = "";
        string _defaultInsertEndCommand = "";

        public string CleanName(string name)
        {
            return name.Replace("-", "_").Replace(" ", "_").Replace("\"", "_").Replace("'", "_").Replace("[", "_").Replace("]", "_").Replace("/", "_").Replace("%", "_").Replace("(", "_").Replace(")", "_");
        }

        public string GetInsertCommand(string sql)
        {
            return Helper.IfNullOrEmpty(InsertStartCommand, _defaultInsertStartCommand) + " " + sql + " " + Helper.IfNullOrEmpty(InsertEndCommand, _defaultInsertEndCommand);
        }

        public DataTable OdbcLoadDataTable(string odbcConnectionString, string sql)
        {
            DataTable table = new DataTable();
            using (OdbcConnection connection = new OdbcConnection(odbcConnectionString))
            {
                connection.Open();
                OdbcDataAdapter adapter = new OdbcDataAdapter(sql, connection);
                adapter.SelectCommand.CommandTimeout = SelectTimeout;
                adapter.Fill(table);
                connection.Close();
            }
            return table;
        }

        public DataTable LoadDataTable(string connectionString, string sql)
        {
            if (MyLoadDataTable != null) return MyLoadDataTable(connectionString, sql);

            DataTable table = new DataTable();

            if (UseDbDataAdapter)
            {
                DbDataAdapter adapter = null;
                var connection = Helper.DbConnectionFromConnectionString(connectionString);
                connection.Open();
                if (connection is OdbcConnection) adapter = new OdbcDataAdapter(sql, (OdbcConnection)connection);
                else adapter = new OleDbDataAdapter(sql, (OleDbConnection)connection);
                adapter.SelectCommand.CommandTimeout = SelectTimeout;
                adapter.Fill(table);
            }
            else
            {
                DbCommand cmd = new OdbcCommand();
                var connection = Helper.DbConnectionFromConnectionString(connectionString);
                connection.Open();
                if (connection is OdbcConnection) cmd = new OdbcCommand(sql, (OdbcConnection)connection);
                else cmd = new OleDbCommand(sql, (OleDbConnection)connection);
                cmd.CommandTimeout = 0;
                cmd.CommandType = CommandType.Text;
                DbDataReader dr = cmd.ExecuteReader();
                DataTable schemaTable = dr.GetSchemaTable();
                foreach (DataRow dataRow in schemaTable.Rows)
                {
                    DataColumn dataColumn = new DataColumn();
                    dataColumn.ColumnName = dataRow["ColumnName"].ToString();
                    dataColumn.DataType = Type.GetType(dataRow["DataType"].ToString());
                    dataColumn.ReadOnly = (bool)dataRow["IsReadOnly"];
                    dataColumn.AutoIncrement = (bool)dataRow["IsAutoIncrement"];
                    dataColumn.Unique = (bool)dataRow["IsUnique"];

                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        if (dataColumn.ColumnName == table.Columns[i].ColumnName)
                        {
                            dataColumn.ColumnName += "_" + table.Columns.Count.ToString();
                        }
                    }
                    table.Columns.Add(dataColumn);
                }

                while (dr.Read())
                {
                    DataRow dataRow = table.NewRow();
                    for (int i = 0; i < table.Columns.Count; i++)
                    {
                        dataRow[i] = dr[i];
                    }
                    table.Rows.Add(dataRow);
                }
            }

            return table;
        }

        public DataTable LoadDataTableFromExcel(string excelPath, string tabName = "")
        {
            if (MyLoadDataTableFromExcel != null) return MyLoadDataTableFromExcel(excelPath, tabName);

            //Copy the Excel file if it is open...
            FileHelper.PurgeTempApplicationDirectory();
            string newPath = FileHelper.GetTempUniqueFileName(excelPath);
            File.Copy(excelPath, newPath, true);
            File.SetLastWriteTime(newPath, DateTime.Now);

            string connectionString = string.Format(ExcelOdbcDriver, newPath);
            string sql = string.Format("select * from [{0}$]", Helper.IfNullOrEmpty(tabName, "Sheet1"));
            return OdbcLoadDataTable(connectionString, sql);
        }

        public DataTable LoadDataTableFromCSV(string csvPath, char? separator = null)
        {
            if (MyLoadDataTableFromCSV != null) return MyLoadDataTableFromCSV(csvPath, separator);

            DataTable result = null;
            bool isHeader = true;
            Regex regexp = null;

            string[] lines = null;
            try
            {
                lines = File.ReadAllLines(csvPath, DefaultEncoding);
            }
            catch
            {
                //Try by copying the file...
                string newPath = FileHelper.GetTempUniqueFileName(csvPath);
                File.Copy(csvPath, newPath);
                lines = File.ReadAllLines(newPath, DefaultEncoding);
                FileHelper.PurgeTempApplicationDirectory();
            }


            foreach (string line in lines)
            {
                var line2 = line.Trim();
                if (string.IsNullOrWhiteSpace(line2)) continue;

                if (regexp == null)
                {
                    if (separator == null)
                    {
                        //use the first line to determine the separator between , and ;
                        separator = ',';
                        if (line2.Split(';').Length > line2.Split(',').Length) separator = ';';
                    }
                    var sep2 = (separator.Value == '|' || separator.Value == ':' ? "\\" : "") + separator.Value;
                    string exp = "(?<=^|" + sep2 + ")(\"(?:[^\"]|\"\")*\"|[^" + sep2 + "]*)";
                    regexp = new Regex(exp);
                }

                MatchCollection collection = regexp.Matches(line2);
                if (isHeader)
                {
                    result = new DataTable();
                    for (int i = 0; i < collection.Count; i++)
                    {
                        result.Columns.Add(new DataColumn(ExcelHelper.FromCsv(collection[i].Value), typeof(string)));
                    }
                    isHeader = false;
                }
                else
                {
                    var row = result.Rows.Add();
                    for (int i = 0; i < collection.Count && i < result.Columns.Count; i++)
                    {
                        row[i] = ExcelHelper.FromCsv(collection[i].Value);
                        if (row[i].ToString().Contains("\0")) row[i] = "";
                    }
                }
            }

            return result;
        }


        public DataTable LoadDataTableFromCSV2(string csvPath, char? separator = null)
        {
            if (MyLoadDataTableFromCSV != null) return MyLoadDataTableFromCSV(csvPath, separator);

            DataTable result = null;
            bool isHeader = true;
            TextFieldParser csvParser = null;
            try
            {
                csvParser = new TextFieldParser(csvPath, DefaultEncoding);
            }
            catch
            {
                //Try by copying the file...
                string newPath = FileHelper.GetTempUniqueFileName(csvPath);
                File.Copy(csvPath, newPath);
                csvParser = new TextFieldParser(newPath, DefaultEncoding);
                FileHelper.PurgeTempApplicationDirectory();
            }
            if (separator == null) separator = ',';
            //csvParser.CommentTokens = new string[] { "#" };
            csvParser.SetDelimiters(new string[] { separator.ToString() });
            csvParser.HasFieldsEnclosedInQuotes = true;

            while (!csvParser.EndOfData)
            {
                string[] fields = csvParser.ReadFields();
                if (isHeader)
                {
                    result = new DataTable();
                    for (int i = 0; i < fields.Length; i++)
                    {
                        result.Columns.Add(new DataColumn(fields[i], typeof(string)));
                    }
                    isHeader = false;
                }
                else
                {
                    var row = result.Rows.Add();
                    for (int i = 0; i < fields.Length && i < result.Columns.Count; i++)
                    {
                        row[i] = fields[i];
                        if (row[i].ToString().Contains("\0")) row[i] = "";
                    }
                }
            }
            csvParser.Close();

            return result;
        }

        public DatabaseType DatabaseType = DatabaseType.MSSQLServer;
        public void SetDatabaseDefaultConfiguration(DatabaseType type)
        {
            DatabaseType = type;
            if (type == DatabaseType.Oracle)
            {
                _defaultColumnCharType = "varchar2";
                _defaultColumnNumericType = "number(18,5)";
                _defaultColumnIntegerType = "number(12)";
                _defaultColumnDateTimeType = "date";
                _defaultInsertStartCommand = "begin";
                _defaultInsertEndCommand = "end;";
            }
            else
            {
                //Default, tested on SQLServer...
                _defaultColumnCharType = "varchar";
                _defaultColumnNumericType = "numeric(18,5)";
                _defaultColumnIntegerType = "int";
                _defaultColumnDateTimeType = "datetime2";
                _defaultInsertStartCommand = "";
                _defaultInsertEndCommand = "";
            }
        }


        public void ExecuteCommand(DbCommand command)
        {
            if (DebugMode) DebugLog.AppendLine("Executing SQL Command\r\n" + command.CommandText);
            try
            {
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Error executing SQL:\r\n{0}\r\n\r\n{1}", command.CommandText, ex.Message));
            }
        }

        public DbCommand GetDbCommand(DbConnection connection)
        {
            DbCommand result = null;
            if (connection is OdbcConnection) result = ((OdbcConnection)connection).CreateCommand();
            else result = ((OleDbConnection)connection).CreateCommand();
            result.CommandTimeout = SelectTimeout;
            return result;
        }

        public void ExecuteNonQuery(string connectionString, string sql, string commandsSeparator = null)
        {
            DbConnection connection = Helper.DbConnectionFromConnectionString(connectionString);
            connection.Open();
            DbCommand command = GetDbCommand(connection);
            string[] commandTexts = new string[] { sql };
            if (!string.IsNullOrEmpty(commandsSeparator))
            {
                commandTexts = sql.Split(new string[] { commandsSeparator }, StringSplitOptions.RemoveEmptyEntries);
            }
            foreach (var commandText in commandTexts)
            {
                command.CommandText = commandText;
                command.ExecuteNonQuery();
            }
            connection.Close();
        }

        public object ExecuteScalar(string connectionString, string sql)
        {
            DbConnection connection = Helper.DbConnectionFromConnectionString(connectionString);
            connection.Open();
            DbCommand command = GetDbCommand(connection);
            command.CommandText = sql;
            var result = command.ExecuteScalar();
            connection.Close();
            return result;
        }

        public void CreateTable(DbCommand command, DataTable table)
        {
            try
            {
                command.CommandText = string.Format("drop table {0}", CleanName(table.TableName));
                ExecuteCommand(command);
            }
            catch { }
            command.CommandText = GetTableCreateCommand(table);
            ExecuteCommand(command);
        }

        public void InsertTable(DbCommand command, DataTable table, string dateTimeFormat, bool deleteFirst)
        {
            DbTransaction transaction = command.Connection.BeginTransaction();
            int cnt = 0;
            try
            {
                command.Transaction = transaction;
                if (deleteFirst)
                {
                    command.CommandText = string.Format("delete from {0}", CleanName(table.TableName));
                    ExecuteCommand(command);
                }

                StringBuilder sql = new StringBuilder("");
                string sqlTemplate = string.Format("insert into {0} ({1})", CleanName(table.TableName), GetTableColumnNames(table)) + " values ({0});\r\n";
                foreach (DataRow row in table.Rows)
                {
                    sql.AppendFormat(sqlTemplate, GetTableColumnValues(row, dateTimeFormat));
                    cnt++;
                    if (cnt % InsertBurstSize == 0)
                    {
                        command.CommandText = GetInsertCommand(sql.ToString());
                        ExecuteCommand(command);
                        sql = new StringBuilder("");
                    }
                }

                if (sql.Length != 0)
                {
                    command.CommandText = GetInsertCommand(sql.ToString());
                    ExecuteCommand(command);
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }


        public string RootGetTableCreateCommand(DataTable table)
        {
            StringBuilder result = new StringBuilder();
            foreach (DataColumn col in table.Columns)
            {
                if (result.Length > 0) result.Append(',');
                result.AppendFormat("{0} ", GetTableColumnName(col));
                result.Append(GetTableColumnType(col));
                result.Append(" NULL");
            }
            return string.Format("CREATE TABLE {0} ({1})", CleanName(table.TableName), result);
        }


        public string GetTableCreateCommand(DataTable table)
        {
            if (MyGetTableCreateCommand != null) return MyGetTableCreateCommand(table);
            return RootGetTableCreateCommand(table);
        }

        public string RootGetTableColumnNames(DataTable table)
        {
            StringBuilder result = new StringBuilder();
            foreach (DataColumn col in table.Columns)
            {
                if (result.Length > 0) result.Append(',');
                result.AppendFormat("{0}", GetTableColumnName(col));
            }
            return result.ToString();
        }

        public string GetTableColumnNames(DataTable table)
        {
            if (MyGetTableColumnNames != null) return MyGetTableColumnNames(table);
            return RootGetTableColumnNames(table);
        }

        public string RootGetTableColumnName(DataColumn col)
        {
            var result = CleanName(col.ColumnName);
            if (DatabaseType == DatabaseType.MSSQLServer) return "[" + result + "]";
            if (DatabaseType == DatabaseType.Oracle) return Helper.QuoteDouble(result);
            return result;
        }

        public string GetTableColumnName(DataColumn col)
        {
            if (MyGetTableColumnName != null) return MyGetTableColumnName(col);
            return RootGetTableColumnName(col);
        }

        public bool IsNumeric(DataColumn col)
        {
            if (col == null) return false;
            // Make this const
            var numericTypes = new[] { typeof(Byte), typeof(Decimal), typeof(Double), typeof(Int16), typeof(Int32), typeof(Int64), typeof(SByte), typeof(Single), typeof(UInt16), typeof(UInt32), typeof(UInt64) };
            return numericTypes.Contains(col.DataType);
        }

        public string RootGetTableColumnType(DataColumn col)
        {
            StringBuilder result = new StringBuilder();
            if (IsNumeric(col))
            {
                //Check for integer
                bool isInteger = true;
                foreach (DataRow row in col.Table.Rows)
                {
                    int a;
                    if (!row.IsNull(col) && !int.TryParse(row[col].ToString(), out a))
                    {
                        isInteger = false;
                        break;
                    }
                }

                if (isInteger) result.Append(Helper.IfNullOrEmpty(ColumnIntegerType, _defaultColumnIntegerType));
                else result.Append(Helper.IfNullOrEmpty(ColumnNumericType, _defaultColumnNumericType));
            }
            else if (col.DataType.Name == "DateTime" || col.DataType.Name == "Date")
            {
                result.Append(Helper.IfNullOrEmpty(ColumnDateTimeType, _defaultColumnDateTimeType));
            }
            else
            {
                int len = col.MaxLength;
                if (len <= 0) len = ColumnCharLength;
                if (ColumnCharLength <= 0)
                {
                    //auto size
                    len = 1;
                    foreach (DataRow row in col.Table.Rows)
                    {
                        if (row[col].ToString().Length > len) len = row[col].ToString().Length + 1;
                    }

                    if (col.Table.Rows.Count == 0) len = NoRowsCharLength;
                }
                if (ColumnCharLength <= 0 && DatabaseType == DatabaseType.MSSQLServer && len > 8000)
                    result.AppendFormat("{0}(max)", Helper.IfNullOrEmpty(ColumnCharType, _defaultColumnCharType));
                else
                    result.AppendFormat("{0}({1})", Helper.IfNullOrEmpty(ColumnCharType, _defaultColumnCharType), len);
            }
            return result.ToString();
        }

        public string GetTableColumnType(DataColumn col)
        {
            if (MyGetTableColumnType != null) return MyGetTableColumnType(col);
            return RootGetTableColumnType(col);
        }

        public string RootGetTableColumnValues(DataRow row, string dateTimeFormat)
        {
            StringBuilder result = new StringBuilder();
            foreach (DataColumn col in row.Table.Columns)
            {
                if (result.Length > 0) result.Append(',');
                result.Append(GetTableColumnValue(row, col, dateTimeFormat));
            }
            return result.ToString();
        }


        public string GetTableColumnValues(DataRow row, string dateTimeFormat)
        {
            if (MyGetTableColumnValues != null) return MyGetTableColumnValues(row, dateTimeFormat);
            return RootGetTableColumnValues(row, dateTimeFormat);
        }

        public string RootGetTableColumnValue(DataRow row, DataColumn col, string dateTimeFormat)
        {
            StringBuilder result = new StringBuilder();
            if (row.IsNull(col))
            {
                result.Append("NULL");
            }
            else if (IsNumeric(col))
            {
                result.AppendFormat(row[col].ToString().Replace(',', '.'));
            }
            else if (col.DataType.Name == "DateTime" || col.DataType.Name == "Date")
            {
                result.Append(Helper.QuoteSingle(((DateTime)row[col]).ToString(dateTimeFormat)));
            }
            else
            {
                string res = row[col].ToString();
                if (TrimText) res = res.Trim();
                if (RemoveCrLf) res = res.Replace("\r", " ").Replace("\n", " ");
                result.Append(Helper.QuoteSingle(res));
            }

            return result.ToString();
        }

        public string GetTableColumnValue(DataRow row, DataColumn col, string dateTimeFormat)
        {
            if (MyGetTableColumnValue != null) return MyGetTableColumnValue(row, col, dateTimeFormat);
            return RootGetTableColumnValue(row, col, dateTimeFormat);
        }

        public bool AreRowsIdentical(DataRow row1, DataRow row2)
        {
            bool result = true;
            if (row1.ItemArray.Length != row2.ItemArray.Length) result = false;
            else
            {
                for (int j = 0; j < row1.ItemArray.Length && result; j++)
                {
                    if (row1[j].ToString() != row2[j].ToString()) result = false;
                }
            }
            return result;
        }

        public bool AreTablesIdentical(DataTable checkTable1, DataTable checkTable2)
        {
            bool result = true;
            if (checkTable1.Rows.Count != checkTable2.Rows.Count || checkTable1.Columns.Count != checkTable2.Columns.Count) result = false;
            if (checkTable1.Rows.Count != checkTable2.Rows.Count) result = false;
            else
            {
                for (int i = 0; i < checkTable1.Rows.Count && result; i++)
                {
                    if (!AreRowsIdentical(checkTable1.Rows[i], checkTable2.Rows[i])) result = false;
                }
            }
            return result;
        }
    }
}
