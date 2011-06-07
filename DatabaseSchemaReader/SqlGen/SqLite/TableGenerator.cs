﻿using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DatabaseSchemaReader.DataSchema;

namespace DatabaseSchemaReader.SqlGen.SqLite
{
    class TableGenerator : TableGeneratorBase
    {
        public TableGenerator(DatabaseTable table)
            : base(table)
        {
        }

        #region Overrides of TableGeneratorBase

        protected override ISqlFormatProvider SqlFormatProvider()
        {
            return new SqlFormatProvider();
        }

        protected override string WriteDataType(DatabaseColumn column)
        {
            var type = DataTypeWriter.SqLiteDataType(column);
            if (column.IsPrimaryKey && (Table.PrimaryKey == null || Table.PrimaryKey.Columns.Count == 1))
            {
                type += " PRIMARY KEY";
                if (column.IsIdentity) type += " AUTOINCREMENT";
            }
            if (!column.Nullable) type += " NOT NULL";
            //if there's a default value, and it's not a guid generator
            if (!string.IsNullOrEmpty(column.DefaultValue) && !SqlTranslator.IsGuidGenerator(column.DefaultValue))
            {
                var value = SqlTranslator.Fix(column.DefaultValue);
                type += " DEFAULT " + value;
            }

            return type;
        }

        protected override void AddTableConstraints(IList<string> columnList)
        {
            var formatter = SqlFormatProvider();
            if (Table.PrimaryKey != null && Table.PrimaryKey.Columns.Count > 1)
            {
                columnList.Add("PRIMARY KEY (" + GetColumnList(Table.PrimaryKey.Columns) + ")");
            }
            foreach (var uniqueKey in Table.UniqueKeys)
            {
                columnList.Add("UNIQUE (" + GetColumnList(uniqueKey.Columns) + ")");
            }
            foreach (var checkConstraint in Table.CheckConstraints)
            {
                var expression = SqlTranslator.Fix(checkConstraint.Expression);
                columnList.Add("CHECK " + expression);
            }

            //http://www.sqlite.org/foreignkeys.html These aren't enabled by default.
            foreach (var foreignKey in Table.ForeignKeys)
            {
                var referencedTable = foreignKey.ReferencedTable(Table.DatabaseSchema);
                //can't find the table. Don't write the fk reference.
                if (referencedTable == null) continue;
                string refColumnList;
                if (referencedTable.PrimaryKey == null && referencedTable.PrimaryKeyColumn != null)
                {
                    refColumnList = referencedTable.PrimaryKeyColumn.Name;
                }
                else if (referencedTable.PrimaryKey == null)
                {
                    continue; //can't find the primary key
                }
                else
                {
                    refColumnList = GetColumnList(referencedTable.PrimaryKey.Columns);
                }

                columnList.Add(string.Format(CultureInfo.InvariantCulture,
                    "FOREIGN KEY ({0}) REFERENCES {1} ({2})",
                    GetColumnList(foreignKey.Columns),
                    formatter.Escape(foreignKey.RefersToTable),
                    refColumnList));
            }
        }

        protected override string NonNativeAutoIncrementWriter()
        {
            return string.Empty;
        }

        protected override string ConstraintWriter()
        {
            return string.Empty;
        }

        #endregion

        private string GetColumnList(IEnumerable<string> columns)
        {
            var escapedColumnNames = columns.Select(column => SqlFormatProvider().Escape(column)).ToArray();
            return string.Join(", ", escapedColumnNames);
        }
    }
}