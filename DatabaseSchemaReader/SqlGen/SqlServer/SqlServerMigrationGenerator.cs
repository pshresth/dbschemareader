﻿using System.Globalization;
using System.Linq;
using System.Text;
using DatabaseSchemaReader.DataSchema;

namespace DatabaseSchemaReader.SqlGen.SqlServer
{
    class SqlServerMigrationGenerator : MigrationGenerator
    {
        public SqlServerMigrationGenerator()
            : base(SqlType.SqlServer)
        {
        }

        protected override string AlterColumnFormat
        {
            get { return "ALTER TABLE {0} ALTER COLUMN {1};"; }
        }
        protected override bool AlterColumnIncludeDefaultValue { get { return false; } }

        public override string AlterColumn(DatabaseTable databaseTable, DatabaseColumn databaseColumn, DatabaseColumn originalColumn)
        {
            var sb = new StringBuilder();
            var defaultName = "DF_" + databaseTable.Name + "_" + databaseColumn.Name;
            if (originalColumn != null)
            {
                if (originalColumn.DefaultValue != null)
                {
                    //have to drop default contraint
                    var df = FindDefaultConstraint(databaseTable, databaseColumn.Name);
                    if (df != null)
                    {
                        defaultName = df.Name;
                        sb.AppendLine("ALTER TABLE " + TableName(databaseTable)
                                      + " DROP CONSTRAINT " + Escape(defaultName) + ";");
                    }
                }
            }
            //we could check if any of the properties are changed here
            sb.AppendLine(base.AlterColumn(databaseTable, databaseColumn, originalColumn));
            if (databaseColumn.DefaultValue != null)
            {
                //add default contraint
                sb.AppendLine("ALTER TABLE " + TableName(databaseTable) +
                    " ADD CONSTRAINT " + Escape(defaultName) +
                    " DEFAULT " + databaseColumn.DefaultValue +
                    " FOR " + Escape(databaseColumn.Name) + ";");
            }

            return sb.ToString();
        }

        public override string DropColumn(DatabaseTable databaseTable, DatabaseColumn databaseColumn)
        {
            var dropColumn = base.DropColumn(databaseTable, databaseColumn);
            //if has a default constraint, drop that first.
            if (databaseColumn.DefaultValue != null)
            {
                var df = FindDefaultConstraint(databaseTable, databaseColumn.Name);
                if (df != null)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("ALTER TABLE " + TableName(databaseTable)
                                  + " DROP CONSTRAINT " + Escape(df.Name) + ";");
                    sb.AppendLine(dropColumn);
                    dropColumn = sb.ToString();
                }
            }
            return dropColumn;
        }

        private static DatabaseConstraint FindDefaultConstraint(DatabaseTable databaseTable, string databaseColumnName)
        {
            return databaseTable.DefaultConstraints
                .FirstOrDefault(c => c.Columns.Contains(databaseColumnName));
        }

        public override string DropDefault(DatabaseTable databaseTable, DatabaseColumn databaseColumn)
        {
            //there is no "DROP DEFAULT" in SqlServer (there is in SQLServer CE). 
            //You must use the default constraint name (which is probably autogenerated)
            var sb = new StringBuilder();
            sb.AppendLine("-- drop default for " + databaseColumn.Name);
            var df = FindDefaultConstraint(databaseTable, databaseColumn.Name);
            if (df != null)
            {
                sb.AppendLine("ALTER TABLE " + TableName(databaseTable)
                              + " DROP CONSTRAINT " + Escape(df.Name) + ";");
            }
            return sb.ToString();
        }
        public override string AddTrigger(DatabaseTable databaseTable, DatabaseTrigger trigger)
        {
            //sqlserver: 
            //CREATE TRIGGER (triggerName) 
            //ON (tableName) 
            //(FOR | AFTER | INSTEAD OF) ( [INSERT ] [ , ] [ UPDATE ] [ , ] [ DELETE ])
            //AS (sql_statement); GO 

            //nicely, SQLServer gives you the entire sql including create statement in TriggerBody
            if (string.IsNullOrEmpty(trigger.TriggerBody))
                return "-- add trigger " + trigger.Name;

            return trigger.TriggerBody + ";";
        }

        public override string RenameColumn(DatabaseTable databaseTable, DatabaseColumn databaseColumn, string originalColumnName)
        {
            if (string.IsNullOrEmpty(originalColumnName) || databaseColumn == null)
                return base.RenameColumn(databaseTable, databaseColumn, originalColumnName);
            var name = TableName(databaseTable) + "." + Escape(originalColumnName);
            return "sp_rename '" + name + "', '" + Escape(databaseColumn.Name) + "', 'COLUMN';";
        }

        public override string RenameTable(DatabaseTable databaseTable, string originalTableName)
        {
            if (string.IsNullOrEmpty(originalTableName) || databaseTable == null)
                return base.RenameTable(databaseTable, originalTableName);
            var name = SchemaPrefix(databaseTable.SchemaOwner) + Escape(originalTableName);
            return "sp_rename '" + name + "', '" + Escape(databaseTable.Name) + "';";
        }

        public override string DropIndex(DatabaseTable databaseTable, DatabaseIndex index)
        {
            //no schema on index name, only on table
            return string.Format(CultureInfo.InvariantCulture,
                "DROP INDEX {0} ON {1};",
                Escape(index.Name),
                TableName(databaseTable));
        }
    }
}
