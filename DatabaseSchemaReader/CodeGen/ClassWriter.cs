﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DatabaseSchemaReader.DataSchema;

namespace DatabaseSchemaReader.CodeGen
{
    public class ClassWriter
    {
        private readonly DatabaseTable table;
        private readonly ClassBuilder classBuilder;
        private DataAnnotationWriter _dataAnnotationWriter;
        private readonly CodeWriterSettings codeWriterSettings;
        private DatabaseTable _inheritanceTable;
        //private CodeInserter _codeInserter;

        public ClassWriter(DatabaseTable table, CodeWriterSettings codeWriterSettings)
        {
            this.codeWriterSettings = codeWriterSettings;
            this.table = table;
            classBuilder = new ClassBuilder();
        }

        public string Write()
        {
            var codeTarget = codeWriterSettings.CodeTarget;
            _dataAnnotationWriter = new DataAnnotationWriter(false, codeWriterSettings);

            if (string.IsNullOrEmpty(table.NetName) && table.DatabaseSchema != null)
            {
                PrepareSchemaNames.Prepare(table.DatabaseSchema, codeWriterSettings.Namer);
            }

            _inheritanceTable = table.FindInheritanceTable();

            codeWriterSettings.CodeInserter.WriteNamespaces(table, classBuilder);
            CodeWriterUtils.WriteFileHeader(classBuilder);
            WriteUsings();
            CodeWriterUtils.BeginNestNamespace(classBuilder, codeWriterSettings);
            
            var tableOrView = table is DatabaseView ? "view" : "table";
            var comment = "Class representing the " + table.Name + " " + tableOrView + ".";
            var classDefinition = $"public class {table.NetName}";
            if (_inheritanceTable != null)
            {
                classDefinition += " : " + _inheritanceTable.NetName;
            }

            codeWriterSettings.CodeInserter.WriteTableAnnotations(table, classBuilder);

            classBuilder.AppendXmlSummary(comment);
            classBuilder.AppendLine($"[Table(\"\\\"{table.Name}\\\"\")]");
            using (classBuilder.BeginNest(classDefinition, comment))
            {
                WriteAllMembers();
            }

            if (!string.IsNullOrEmpty(codeWriterSettings.Namespace))
            {
                classBuilder.EndNest();
            }

            return classBuilder.ToString();
        }

        private void WriteAllMembers()
        {
            codeWriterSettings.CodeInserter.WriteClassMembers(table, classBuilder);

            classBuilder.AppendLine("public IDbContext DbContext { get; set; }");
            classBuilder.AppendLine("");

            if (_inheritanceTable == null)
            {
                WritePrimaryKey();
            }

            foreach (var column in table.Columns)
            {
                if (column.IsPrimaryKey) continue;
                //if (column.IsForeignKey) continue;
                WriteColumn(column);
            }

            foreach (var foreignKey in table.ForeignKeys)
            {
                WriteForeignKey(foreignKey);
            }

            WriteForeignKeyCollections();

            WriteWiths();
        }

        private void WriteWiths()
        {
            foreach (var foreignKey in table.ForeignKeys)
            {
                WriteWith(foreignKey);
                WriteWithByCustomer(foreignKey);
            }

            foreach (var foreignKey in table.ForeignKeyChildren)
            {
                WriteWith(foreignKey);
                WriteWithByCustomer(foreignKey);
            }
        }

        public void WriteWithCommon(IEnumerable<Parameter> _methodParameters, DatabaseTable foreignKeyChild)
        {
            // TODO: KE
            foreach (var fk in CodeWriterUtils.GetWithForeignKeys(table, foreignKeyChild))
            {
                var dataType = foreignKeyChild.NetName;
                if (fk.Columns.Count != fk.ReferencedColumns(table.DatabaseSchema).Count())
                {
                    throw new InvalidOperationException("Number of foreign key columns does not match number of columns referenced!");
                }

                var referencedColumns = fk.ReferencedColumns(table.DatabaseSchema).ToList();
                var methodParameters = new List<Tuple<string, string, string>>();
                for (var i = 0; i < fk.Columns.Count; i++)
                {
                    var refColumn = fk.Columns[i];
                    var column = referencedColumns[i];
                    var actualColumn = table.Columns.Single(tc => tc.Name == column);
                    var dataTypeForParameter = DataTypeWriter.FindDataType(actualColumn);
                    methodParameters.Add(new Tuple<string, string, string>(codeWriterSettings.Namer.NameToAcronym(refColumn), dataTypeForParameter, refColumn));
                }

                classBuilder.BeginNest($"public {CodeWriterUtils.GetWithMethodSignature(table, fk, codeWriterSettings)}");

                var methodCallParameters = new List<string>
                {
                    "DbContext"
                };

                foreach (var fkc in fk.Columns)
                {
                    var tc = table.Columns.Single(_tc => _tc.Name == fkc);
                    var parameter = $"{CodeWriterUtils.GetPropertyNameForDatabaseColumn(tc)}";
                    if (DataTypeWriter.FindDataType(tc).EndsWith("?"))
                    {
                        parameter += ".Value";
                    }

                    methodCallParameters.Add(parameter);
                }

                var s = string.Join(", ", methodCallParameters);
                var methodName = $"GetListBy{string.Join("And", methodParameters.Select(mp => codeWriterSettings.Namer.NameColumnAsMethodTitle(mp.Item3)))}";
                var propertyName = codeWriterSettings.Namer.ForeignKeyCollectionName(table.Name, foreignKeyChild, fk);
                classBuilder.AppendLine($"{propertyName} = {CodeWriterUtils.GetRepositoryImplementationName(foreignKeyChild)}.{methodName}({s});");
                classBuilder.AppendLine("return this;");
                classBuilder.EndNest();
                classBuilder.AppendLine("");
            }
        }

        public void WriteWithByCustomer(DatabaseTable foreignKeyChild)
        {
            var methodParametersByCustomer = CodeWriterUtils.GetWithMethodParameters(foreignKeyChild, codeWriterSettings, true);
            if (methodParametersByCustomer == null || !methodParametersByCustomer.Any())
            {
                return;
            }

            WriteWithCommon(methodParametersByCustomer, foreignKeyChild);
        }

        public void WriteWith(DatabaseTable foreignKeyChild)
        {
            var methodParameters = CodeWriterUtils.GetWithMethodParameters(table, foreignKeyChild, codeWriterSettings, false);
            WriteWithCommon(methodParameters, foreignKeyChild);
        }

        public void WriteWithCommon(IEnumerable<Parameter> methodParameters, DatabaseConstraint foreignKey)
        {
            var refTable = foreignKey.ReferencedTable(table.DatabaseSchema);
            var dataType = refTable.NetName;

            if (foreignKey.Columns.Count != foreignKey.ReferencedColumns(table.DatabaseSchema).Count())
            {
                throw new InvalidOperationException("Number of foreign key columns does not match number of columns referenced!");
            }

            classBuilder.BeginNest($"public {CodeWriterUtils.GetWithMethodSignature(table, foreignKey, codeWriterSettings, methodParameters)}");

            var methodCallParameters = new List<string>
                                           {
                                               "DbContext"
                                           };
            var propertyName = codeWriterSettings.Namer.ForeignKeyName(table, foreignKey);
            foreach (var fkc in foreignKey.Columns)
            {
                var tc = table.Columns.Single(_tc => _tc.Name == fkc);
                var parameter = $"{CodeWriterUtils.GetPropertyNameForDatabaseColumn(tc)}";
                if (tc.Nullable && DataTypeWriter.FindDataType(tc).EndsWith("?")) // KE: need the check for the "?" so that we correctly handle reference types like string
                {
                    using (classBuilder.BeginNest($"if (!{parameter}.HasValue)"))
                    {
                        classBuilder.AppendLine($"{propertyName} = null;");
                        classBuilder.AppendLine("return this;");
                    }

                    classBuilder.AppendLine("");
                    parameter += ".Value";
                }

                methodCallParameters.Add(parameter);
            }

            methodCallParameters.AddRange(methodParameters.Select(mp => mp.Name));
            var s = string.Join(", ", methodCallParameters);
            classBuilder.AppendLine($"{propertyName} = {CodeWriterUtils.GetRepositoryImplementationName(foreignKey.ReferencedTable(table.DatabaseSchema))}.Get({s});");
            classBuilder.AppendLine("return this;");
            classBuilder.EndNest();
            classBuilder.AppendLine("");
        }

        public void WriteWithByCustomer(DatabaseConstraint foreignKey)
        {
            var methodParametersByCustomer = CodeWriterUtils.GetWithMethodParameters(table, codeWriterSettings, true);
            if (methodParametersByCustomer == null || !methodParametersByCustomer.Any())
            {
                return;
            }

            WriteWithCommon(methodParametersByCustomer, foreignKey);
        }

        public void WriteWith(DatabaseConstraint foreignKey)
        {
            var methodParameters = CodeWriterUtils.GetWithMethodParameters(table, codeWriterSettings, false);
            WriteWithCommon(methodParameters, foreignKey);
        }

        private void WritePrimaryKey()
        {
            foreach (var column in table.Columns.Where(c => c.IsPrimaryKey))
            {
                WriteColumn(column, false);
            }
        }

        private void WriteUsings()
        {
            classBuilder.AppendLine("using System;");
            classBuilder.AppendLine("using System.Collections.Generic;");
            classBuilder.AppendLine("using System.ComponentModel.DataAnnotations;");
            classBuilder.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
            if (table.Columns.Select(c => c.DataType.IsGeospatial).Contains(true))
            {
                classBuilder.AppendLine("using NetTopologySuite.Geometries;");
            }

            classBuilder.AppendLine("using PeopleNet.EnterpriseData.DataAccess.Repositories;");
            classBuilder.AppendLine("");
        }

        private void WriteForeignKeyCollections()
        {
            var listType = "IEnumerable<";

            var hasTablePerTypeInheritance =
                (table.ForeignKeyChildren.Count(fk => table.IsSharedPrimaryKey(fk)) > 1);

            foreach (var foreignKey in table.ForeignKeyChildren)
            {
                if (foreignKey.IsManyToManyTable() && codeWriterSettings.CodeTarget == CodeTarget.PocoEntityCodeFirst)
                {
                    WriteManyToManyCollection(foreignKey);
                    continue;
                }
                if (table.IsSharedPrimaryKey(foreignKey))
                {
                    if (hasTablePerTypeInheritance)
                        continue;
                    //type and property name are the same
                    classBuilder.AppendAutomaticProperty(foreignKey.NetName, foreignKey.NetName, true);
                    continue;
                }

                //the other table may have more than one fk pointing at this table
                var fks = table.InverseForeignKeys(foreignKey);
                foreach (var fk in fks)
                {
                    var propertyName = codeWriterSettings.Namer.ForeignKeyCollectionName(table.Name, foreignKey, fk);
                    var dataType = listType + foreignKey.NetName + ">";
                    WriteForeignKeyChild(propertyName, dataType);
                    classBuilder.AppendLine("");
                }
            }
        }

        private void WriteForeignKeyChild(string propertyName, string dataType)
        {
            if (codeWriterSettings.CodeTarget == CodeTarget.PocoRiaServices)
                classBuilder.AppendLine("[Include]");
            classBuilder.AppendAutomaticCollectionProperty(dataType, propertyName, false);
        }

        private void WriteManyToManyCollection(DatabaseTable foreignKey)
        {
            //look over the junction table to find the other many-to-many end
            var target = foreignKey.ManyToManyTraversal(table);
            if (target == null)
            {
                Debug.WriteLine("Can't navigate the many to many relationship for " + table.Name + " to " + foreignKey.Name);
                return;
            }
            var propertyName = codeWriterSettings.Namer.NameCollection(target.NetName);
            var dataType = "ICollection<" + target.NetName + ">";
            classBuilder.AppendAutomaticCollectionProperty(dataType, propertyName, false);

        }

        private void WriteManyToManyInitialize(DatabaseTable foreignKey)
        {
            //look over the junction table to find the other many-to-many end
            var target = foreignKey.ManyToManyTraversal(table);
            if (target == null)
            {
                return;
            }
            var propertyName = codeWriterSettings.Namer.NameCollection(target.NetName);
            var dataType = "List<" + target.NetName + ">";
            classBuilder.AppendLine(propertyName + " = new " + dataType + "();");
        }

        private void InitializeCollectionsInConstructor()
        {
            if (!table.ForeignKeyChildren.Any()) return;
            using (classBuilder.BeginNest("public " + table.NetName + "()"))
            {
                foreach (var foreignKey in table.ForeignKeyChildren)
                {
                    if (foreignKey.IsManyToManyTable() && codeWriterSettings.CodeTarget == CodeTarget.PocoEntityCodeFirst)
                    {
                        WriteManyToManyInitialize(foreignKey);
                        continue;
                    }
                    if (table.IsSharedPrimaryKey(foreignKey))
                    {
                        continue;
                    }
                    var fks = table.InverseForeignKeys(foreignKey);
                    foreach (DatabaseConstraint fk in fks)
                    {
                        var propertyName = codeWriterSettings.Namer.ForeignKeyCollectionName(table.Name, foreignKey, fk);
                        var dataType = "List<" + foreignKey.NetName + ">";
                        classBuilder.AppendLine(propertyName + " = new " + dataType + "();");
                    }
                }
            }
            classBuilder.AppendLine("");
        }

        private void WriteColumn(DatabaseColumn column)
        {
            WriteColumn(column, false);
        }

        private void WriteColumn(DatabaseColumn column, bool notNetName)
        {
            var propertyName = CodeWriterUtils.GetPropertyNameForDatabaseColumn(column);
            var dataType = DataTypeWriter.FindDataType(column);

            if (notNetName)
            {
                //in EF, you want a fk Id property
                //must not conflict with entity fk name
                propertyName += "Id";
            }

            codeWriterSettings.CodeInserter.WriteColumnAnnotations(table, column, classBuilder);
            _dataAnnotationWriter.Write(classBuilder, column, propertyName);
            var useVirtual = true;
            classBuilder.AppendAutomaticProperty(dataType, propertyName, useVirtual);
        }



        private static DatabaseTable FindForeignKeyTable(DatabaseColumn column)
        {
            var refTable = column.ForeignKeyTable;
            if (refTable != null) return refTable;
            //column may be in multiple fks, and the model may be broken
            var table = column.Table;
            if (table == null) return null;
            //find the first foreign key containing this column
            var fk = table.ForeignKeys.FirstOrDefault(c => c.Columns.Contains(column.Name));
            if (fk != null)
            {
                refTable = fk.ReferencedTable(table.DatabaseSchema);
            }
            return refTable;
        }

        private void WriteForeignKey(DatabaseConstraint foreignKey)
        {
            // get the reference table
            var refTable = foreignKey.ReferencedTable(table.DatabaseSchema);

            //we inherit from it instead (problem with self-joins)
            if (Equals(refTable, _inheritanceTable)) return;

            if (refTable == null)
            {
                //we can't find the foreign key table, so just write the columns
                WriteForeignKeyColumns(foreignKey, "");
                return;
            }

            var propertyName = codeWriterSettings.Namer.ForeignKeyName(table, foreignKey);
            var dataType = refTable.NetName;

            classBuilder.AppendAutomaticProperty(dataType, propertyName);

            if (false && codeWriterSettings.UseForeignKeyIdProperties)
            {
                WriteForeignKeyColumns(foreignKey, propertyName);
            }
        }

        private void WriteForeignKeyColumns(DatabaseConstraint foreignKey, string propertyName)
        {
            //for code first, we may have to write scalar properties
            //1 if the fk is also a pk
            //2 if they selected use Foreign Key Ids
            foreach (var columnName in foreignKey.Columns)
            {
                var column = table.FindColumn(columnName);
                if (column == null) continue;
                //primary keys are already been written
                if (!column.IsPrimaryKey)
                {
                    WriteColumn(column, propertyName.Equals(column.NetName));
                }
            }
        }
    }
}
