﻿//
// Copyright (c) Seal Report, Eric Pfirsch (sealreport@gmail.com), http://www.sealreport.org.
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License. http://www.apache.org/licenses/LICENSE-2.0..
//
using System;
using System.Collections.Generic;
using System.Drawing.Design;
using System.ComponentModel;
using System.Windows.Forms.Design;
using System.Windows.Forms;
using Seal.Model;

namespace Seal.Forms
{
    public class SQLEditor : UITypeEditor
    {
        const string descriptionTemplate1 = "Note that Razor script can be used if the text starts with '@'.\r\n";
        const string descriptionTemplate2 = "Note that Razor script can be used if the text starts with '@'.\r\nThe final SQL may contain Shared Restrictions by using the keyword '{SharedRestriction_<Name>}' where <Name> is the Shared Restriction name.\r\nShared Restrictions can then be configured in the Report Models involved.\r\n";
        const string descriptionTemplate3 = "The final SQL may contain Shared Restrictions by using the keyword '{SharedRestriction_<Name>}' where <Name> is the Shared Restriction name.\r\nShared Restrictions can then be configured in the Report Models involved.\r\n";

        const string razorTableTemplate = "@using Seal.Model\r\n@{\r\nMetaTable table = Model;\r\nstring result = \"update Employees set LastName=LastName where {SharedRestriction_LastName}\";\r\n}\r\n@Raw(result)";
        const string razorTableWhereTemplate = @"@using Seal.Model
@using Seal.Helpers
@{
    MetaTable table = Model;
    string restriction = Environment.UserName; //This gives the windows user of the process running the engine
    if (table.Source.Report != null && table.Source.Report.SecurityContext != null)
    {
        var securityContext = table.Source.Report.SecurityContext; //User is logged through a Web Report Servert and has a context
        restriction = securityContext.Name; //Name of the user set during the login
        restriction = securityContext.WebUserName; //Name got from the login window
        //securityContext.SecurityGroups; //List of security groups set for the user
        if (securityContext.BelongsToGroup(""Default Group"")) { //Test if the user belongs to a group
            //Special restriction here
        }
    }
    string result = string.Format(""aColumnName={0}"", Helper.QuoteSingle(restriction));
    }
@Raw(result)";
        const string razorSourceTemplate = "@using Seal.Model\r\n@{\r\nMetaSource source = Model;\r\nstring result = \"update Employees set LastName=LastName\";\r\n}\r\n@Raw(result)";
        const string razorModelTemplate = "@using Seal.Model\r\n@{\r\nReportModel model = Model;\r\nstring result = \"update Employees set LastName=LastName where {SharedRestriction_LastName}\";\r\n}\r\n@Raw(result)";
        const string razorTaskTemplate = "@using Seal.Model\r\n@{\r\nReportTask task= Model;\r\nstring result = \"update Employees set LastName=LastName where {SharedRestriction_LastName}\";\r\n}\r\n@Raw(result)";
        const string razorEnumTemplate = "@using Seal.Model\r\n@{\r\nMetaEnum enumList= Model;\r\nstring result = \"SELECT DISTINCT CategoryID, CategoryName FROM Categories ORDER BY 2\";\r\n}\r\n@Raw(result)";

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            if (context.Instance is MetaEnum && context.PropertyDescriptor.IsReadOnly)
            {
                return UITypeEditorEditStyle.None;
            }

            return UITypeEditorEditStyle.Modal;
        }
        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            var svc = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
            if (svc != null)
            {
                var frm = new SQLEditorForm();
                frm.Instance = context.Instance;
                frm.PropertyName = context.PropertyDescriptor.Name;
                string valueToEdit = (value == null ? "" : value.ToString());
                bool forceValueToEdit = false;
                List<string> samples = new List<string>();
                var description = "";

                if (context.Instance is ReportModel)
                {
                    ReportModel model = context.Instance as ReportModel;
                    model.BuildSQL();
                    if (!string.IsNullOrEmpty(model.ExecutionError)) 
                    {
                        throw new Exception("Error building the SQL Statement...\r\nPlease fix these errors first.\r\n" + model.ExecutionError); 
                    } 

                    if (context.PropertyDescriptor.Name == "PreSQL" || context.PropertyDescriptor.Name == "PostSQL")
                    {
                        samples.Add(razorModelTemplate);
                        samples.Add("update Employees set LastName=LastName");
                        samples.Add("update Employees set LastName=LastName where {SharedRestriction_LastName}");
                        frm.clearToolStripButton.Visible = false;
                        description = descriptionTemplate2;
                    }
                    else
                    {
                        frm.checkSQLToolStripButton.Visible = false;
                        forceValueToEdit = true;
                        var value2 = value;
                        if (value2 == null) value2 = "";
                        if (context.PropertyDescriptor.Name == "SqlSelect" && string.IsNullOrEmpty(value2.ToString())) valueToEdit = model.execSelect;
                        if (context.PropertyDescriptor.Name == "SqlFrom" && string.IsNullOrEmpty(value2.ToString())) valueToEdit = "FROM " + model.execFromClause.ToString();
                        if (context.PropertyDescriptor.Name == "SqlGroupBy" && string.IsNullOrEmpty(value2.ToString())) valueToEdit = "GROUP BY " + model.execGroupByClause.ToString();
                        if (context.PropertyDescriptor.Name == "SqlOrderBy" && string.IsNullOrEmpty(value2.ToString())) valueToEdit = "ORDER BY " + model.execOrderByClause.ToString();
                    }
                }
                else if (context.Instance is MetaJoin)
                {
                    frm.clearToolStripButton.Visible = false;
                }
                else if (context.Instance is MetaEnum)
                {
                    if (context.PropertyDescriptor.Name == "SqlDisplay")
                    {
                        samples.Add("SELECT DISTINCT CategoryID, CategoryName\r\nFROM Categories\r\nWHERE CategoryName like '%{EnumFilter}%'\r\nORDER BY 2");
                        samples.Add("SELECT DISTINCT City\r\nFROM Customers\r\nWHERE Country in ({EnumValues_Country})\r\nORDER BY 1");
                        description = descriptionTemplate1 + "The SQL may contain the filter tag by using the keyword '{EnumFilter}' to build the enum  with filters got from the user.\r\nThe SQL may contain dependencies with other enum values got from the user by using the keyword {EnumValues_<Name>} where <Name> is the name of the other enumerated list.\r\n";
                    }
                    else
                    {

                        samples.Add(razorEnumTemplate);
                        samples.Add("SELECT DISTINCT CategoryID, CategoryName\r\nFROM Categories\r\nORDER BY 2");
                        description = descriptionTemplate1;
                    }
                    frm.clearToolStripButton.Visible = false;
                    MetaEnum anEnum = context.Instance as MetaEnum;
                    if (value == null || string.IsNullOrEmpty(value.ToString()))
                    {
                        forceValueToEdit = true;
                        valueToEdit = "select col1,col2 from table order by col2";
                    }
                }
                else if (context.Instance is ReportSource || context.Instance is MetaSource)
                {
                    samples.Add(razorSourceTemplate);
                    frm.clearToolStripButton.Visible = false;
                    description = descriptionTemplate1;
                }
                else if (context.Instance is MetaTable)
                {
                    if (context.PropertyDescriptor.Name == "PreSQL" || context.PropertyDescriptor.Name == "PostSQL")
                    {
                        samples.Add(razorTableTemplate);
                        samples.Add("update Employees set LastName=LastName");
                        samples.Add("update Employees set LastName=LastName where {SharedRestriction_LastName}");
                        description = descriptionTemplate2;
                    }
                    else if (context.PropertyDescriptor.Name == "WhereSQL")
                    {
                        samples.Add(razorTableWhereTemplate);
                        samples.Add("{SharedRestriction_LastName}");
                        description = descriptionTemplate2;
                    }
                    else if (context.PropertyDescriptor.Name == "Sql")
                    {
                        samples.Add("SELECT * FROM Employees WHERE {SharedRestriction_LastName}");
                        description = descriptionTemplate3;
                    }
                    frm.clearToolStripButton.Visible = false;
                }
                else if (context.Instance is ReportTask)
                {
                    samples.Add(razorTaskTemplate);
                    samples.Add("update Employees set LastName=LastName");
                    description = descriptionTemplate1;
                    frm.clearToolStripButton.Visible = false;
                }

                if (value != null || forceValueToEdit) frm.sqlTextBox.Text = valueToEdit.ToString();

                if (context.PropertyDescriptor.IsReadOnly) frm.SetReadOnly();
                else 
                {
                    frm.SetSamples(samples);
                    if (!string.IsNullOrEmpty(description)) frm.errorTextBox.Text = description;
                }

                if (svc.ShowDialog(frm) == DialogResult.OK)
                {
                    value = frm.sqlTextBox.Text;
                }
            }
            return value;
        }
    }
}
