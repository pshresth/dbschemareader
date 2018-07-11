﻿using DatabaseSchemaReader.DataSchema;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DatabaseSchemaReader.CodeGen
{
    public static class CodeWriterUtils
    {
        public const string CustomerTableName = "Customer";
        public const string CustomerIDColumnName = "CustomerID";
        public const string CustomerAssetOrganizationIDColumnName = "CustomerAssetOrganizationID";
        public const string CustomerAssetOrganizationTableName = "CustomerAssetOrganization";
        public const string BaseMethodNameCreate = "Create";
        public const string BaseMethodNameGet = "Get";
        public const string BaseMethodNameUpdate = "Update";
        public const string BaseMethodNameDelete = "Delete";

        public static void WriteFileHeader(ClassBuilder classBuilder)
        {
            classBuilder.AppendLine(@"//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated by a Tool.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
//
//    Behavior of class members defined in this file may be changed by overriding in a derived class.
// </auto-generated>
//------------------------------------------------------------------------------");
        }

        public static IEnumerable<DatabaseTable> GetAllForeignTables(DatabaseTable table)
        {
            var tables = new List<DatabaseTable>();
            foreach (var fk in table.ForeignKeys)
            {
                tables.Add(fk.ReferencedTable(table.DatabaseSchema));
            }

            foreach (var t in table.ForeignKeyChildren)
            {
                tables.Add(t);
            }

            return tables;
        }

        public static IEnumerable<Parameter> GetTablesAsParameters(IEnumerable<DatabaseTable> tables)
        {
            var fields = new List<Parameter>();
            foreach (var t in tables.Distinct().OrderBy(t => t.Name))
            {
                var field = new Parameter
                                {
                                    ColumnNameToQueryBy = null,
                                    DataType = CodeWriterUtils.GetRepositoryInterfaceName(t),
                                    Name = NameFixer.ToCamelCase(CodeWriterUtils.GetRepositoryImplementationName(t))
                                };

                fields.Add(field);
            }

            return fields;
        }

        public static void DeduplicateMethodParameterNames(IList<Parameter> methodParameters)
        {
            var uniqueCount = methodParameters.Select(item => item.Name).Distinct().Count();
            var count = methodParameters.Count();
            var noDupes = uniqueCount == count;
            if (noDupes)
            {
                return;
            }

            var duplicateNameGroups = methodParameters.GroupBy(item => item.Name).Where(item => item.Count() > 1);
            duplicateNameGroups.ToList().ForEach(duplicateNameGroup =>
            {
                var firstOne = true;
                var uniqueNumber = 2;
                for (var i = 0; i < methodParameters.Count(); i++)
                {
                    var parameter = methodParameters[i];
                    if (parameter.Name != duplicateNameGroup.Key)
                    {
                        continue;
                    }

                    if (firstOne)
                    {
                        firstOne = false;
                        continue;
                    }

                    parameter.Name = $"{parameter.Name}{uniqueNumber}";
                    methodParameters[i] = parameter;
                    uniqueNumber++;
                }
            });
        }

        public static void BeginNestNamespace(ClassBuilder classBuilder, CodeWriterSettings codeWriterSettings)
        {
            if (!String.IsNullOrEmpty(codeWriterSettings.Namespace))
            {
                classBuilder.BeginNest("namespace " + codeWriterSettings.Namespace);
                return;
            }

            throw new ArgumentNullException("codeWriterSettings.Namespace");
        }

        public static string GetCreateMethodSignature(DatabaseTable table, IEnumerable<Parameter> methodParameters)
        {
            return $"{table.NetName} Create({PrintParametersForSignature(methodParameters)})";
        }

        public static IEnumerable<Parameter> GetCreateMethodParameters(DatabaseTable table)
        {
            return new List<Parameter> { GetEntityParameter(table, "An entity to insert.") };
        }

        public static Parameter GetDbContextMethodParameter()
        {
            return new Parameter()
            {
                DataType = "IDbContext",
                Name = "dbContext",
                Summary = "A database context."
            };
        }

        public static IEnumerable<Parameter> AddDbContextParameter(IEnumerable<Parameter> parameters)
        {
            var p = parameters.ToList();
            p.Insert(0, GetDbContextMethodParameter());
            return p;
        }

        public static string GetGetMethodSignature(DatabaseTable table, CodeWriterSettings codeWriterSettings, IEnumerable<Parameter> methodParameters)
        {
            var methodName = GetMethodName(methodParameters, codeWriterSettings, true, BaseMethodNameGet);
            return $"{table.NetName} {methodName}({PrintParametersForSignature(methodParameters)})";
        }

        public static IEnumerable<Parameter> GetGetMethodParameters(DatabaseTable table, CodeWriterSettings codeWriterSettings, bool byCustomer, bool forUniqueConstraint)
        {
            if (!forUniqueConstraint)
            {
                return GetMethodParametersForPrimaryKeys(table, codeWriterSettings, byCustomer);
            }

            return GetMethodParametersForUniqueConstraint(table, codeWriterSettings, byCustomer);
        }

        public static string GetGetListMethodSignature(DatabaseTable table, CodeWriterSettings codeWriterSettings, IEnumerable<Parameter> methodParameters)
        {
            //return $"IEnumerable<{table.NetName}> GetList({PrintParametersForSignature(methodParameters)})";
            var methodName = GetMethodName(methodParameters, codeWriterSettings, false, BaseMethodNameGet);
            return $"IEnumerable<{table.NetName}> {methodName}({PrintParametersForSignature(methodParameters)})";
        }

        public static IEnumerable<Parameter> GetGetListMethodParameters(DatabaseTable table, CodeWriterSettings codeWriterSettings, bool byCustomer)
        {
            var columns = new List<DatabaseColumn>();
            if (byCustomer)
            {
                if (TableHasOrgUnitForeignKey(table))
                {
                    var orgUnitTable = table.DatabaseSchema.FindTableByName(CustomerAssetOrganizationTableName);
                    columns.Add(orgUnitTable.FindColumn(CustomerIDColumnName));
                }
                else
                {
                    return new List<Parameter>();
                }

                return GetMethodParametersForColumns(columns, codeWriterSettings);
            }

            return new List<Parameter>();
        }

        public static string GetGetListByMethodSignature(DatabaseTable table, IEnumerable<DatabaseColumn> columns, CodeWriterSettings codeWriterSettings, IEnumerable<Parameter> methodParameters)
        {
            //return $"IEnumerable<{table.NetName}> {GetGetMethodName(columns, codeWriterSettings)}({PrintParametersForSignature(methodParameters)})";
            var methodName = GetGetMethodName(columns, codeWriterSettings, false);
            return $"IEnumerable<{table.NetName}> {methodName}({PrintParametersForSignature(methodParameters)})";
        }

        public static string ConvertParametersToMethodNameByPart(IEnumerable<Parameter> methodParameters, CodeWriterSettings codeWriterSettings)
        {
            var s = new List<string>();
            foreach (var p in methodParameters)
            {
                if (!String.IsNullOrEmpty(p.ColumnNameToQueryBy))
                {
                    var properName = codeWriterSettings.Namer.NameColumnAsMethodTitle(p.ColumnNameToQueryBy);
                    s.Add(properName);
                }
            }

            return s.Any() ? String.Join("And", s) : String.Empty;
        }

        public static string GetMethodName(IEnumerable<Parameter> methodParameters, CodeWriterSettings codeWriterSettings, bool singular, string baseMethodName)
        {
            var partialMethodName = ConvertParametersToMethodNameByPart(methodParameters, codeWriterSettings);
            var methodName = singular ? baseMethodName : $"{baseMethodName}List";
            return !String.IsNullOrEmpty(partialMethodName) ? $"{methodName}By{partialMethodName}" : methodName;
        }

        public static string GetGetMethodName(IEnumerable<DatabaseColumn> columns, CodeWriterSettings codeWriterSettings, bool singular)
        {
            var methodParameters = GetGetListByMethodParameters(columns, codeWriterSettings);
            return GetMethodName(methodParameters, codeWriterSettings, singular, BaseMethodNameGet);
        }

        public static IEnumerable<Parameter> GetGetListByMethodParameters(IEnumerable<DatabaseColumn> columns, CodeWriterSettings codeWriterSettings)
        {
            return GetMethodParametersForColumns(columns, codeWriterSettings);
        }

        public static IEnumerable<IEnumerable<DatabaseColumn>> GetGetListByColumnCombinations(DatabaseTable table)
        {
            var primaryKeyColumns = GetPrimaryKeyColumns(table);
            List<IEnumerable<DatabaseColumn>> combinations = null;
            var allKeys = new List<DatabaseColumn>();
            if (primaryKeyColumns.Count() > 1)
            {
                allKeys.AddRange(primaryKeyColumns);
            }

            allKeys.AddRange(GetInverseForeignKeyReferencedColumns(table));
            allKeys.AddRange(GetForeignKeyColumns(table));
            for (var i = 1; i <= allKeys.Distinct().Count(); i++)
            {
                if (combinations == null)
                {
                    combinations = new List<IEnumerable<DatabaseColumn>>();
                }

                var c = allKeys.Distinct().DifferentCombinations(i);
                combinations.AddRange(c);
            }

            if (combinations == null)
            {
                return null;
            }

            return OmitUniqueConstraintColumnsFromCombinations(combinations, table);
        }

        public static IEnumerable<IEnumerable<DatabaseColumn>> OmitUniqueConstraintColumnsFromCombinations(IEnumerable<IEnumerable<DatabaseColumn>> combinations, DatabaseTable table)
        {
            if (combinations == null)
            {
                return null;
            }

            var primaryKeyColumns = GetPrimaryKeyColumns(table)?.ToList();
            var uniqueConstraintColumns = GetUniqueConstraintColumns(table)?.ToList();
            uniqueConstraintColumns?.Add(primaryKeyColumns);
            List<IEnumerable<DatabaseColumn>> newCombinations = null;
            foreach (var c in combinations)
            {
                var combinationContainsUniqueConstraint = false;
                foreach (var cols in uniqueConstraintColumns)
                {
                    if (!cols.Except(c).Any())
                    {
                        combinationContainsUniqueConstraint = true;
                        break;
                    }
                }

                if (combinationContainsUniqueConstraint)
                {
                    continue;
                }

                if (newCombinations == null)
                {
                    newCombinations = new List<IEnumerable<DatabaseColumn>>();
                }

                newCombinations.Add(c);
            }

            return newCombinations;
        }

        public static string GetWithMethodSignature(DatabaseTable table, DatabaseConstraint foreignKey, CodeWriterSettings codeWriterSettings)
        {
            var propertyName = codeWriterSettings.Namer.ForeignKeyName(table, foreignKey);
            return $"{table.NetName} With{propertyName}()";
        }

        public static IEnumerable<DatabaseConstraint> GetWithForeignKeys(DatabaseTable table, DatabaseTable foreignKeyChild)
        {
            return foreignKeyChild.ForeignKeys.Where(fk => fk.ReferencedTable(table.DatabaseSchema).Name == table.Name);
        }

        public static string GetWithMethodSignature(DatabaseTable table, DatabaseTable foreignKeyChild, DatabaseConstraint foreignForeignKey, CodeWriterSettings codeWriterSettings)
        {
            var propertyName = codeWriterSettings.Namer.ForeignKeyCollectionName(table.Name, foreignKeyChild, foreignForeignKey);
            return $"{table.NetName} With{propertyName}()";
        }

        public static string GetUpdateMethodSignature(DatabaseTable table, CodeWriterSettings codeWriterSettings, IEnumerable<Parameter> methodParameters)
        {
            var methodName = GetMethodName(methodParameters, codeWriterSettings, true, BaseMethodNameUpdate);
            return $"{table.NetName} {methodName}({PrintParametersForSignature(methodParameters)})";
        }

        public static IEnumerable<Parameter> GetUpdateMethodParameters(DatabaseTable table, CodeWriterSettings codeWriterSettings, bool byCustomer, bool forUniqueConstraint)
        {
            if (!forUniqueConstraint)
            {
                return GetMethodParametersForPrimaryKeys(table, codeWriterSettings, byCustomer);
            }

            return GetMethodParametersForUniqueConstraint(table, codeWriterSettings, byCustomer);
        }

        public static string GetDeleteMethodSignature(DatabaseTable table, CodeWriterSettings codeWriterSettings, IEnumerable<Parameter> methodParameters)
        {
            var methodName = GetMethodName(methodParameters, codeWriterSettings, true, BaseMethodNameDelete);
            return $"{table.NetName} {methodName}({PrintParametersForSignature(methodParameters)})";
        }

        public static IEnumerable<Parameter> GetDeleteMethodParameters(DatabaseTable table, CodeWriterSettings codeWriterSettings, bool byCustomer, bool forUniqueConstraint)
        {
            if (!forUniqueConstraint)
            {
                return GetMethodParametersForPrimaryKeys(table, codeWriterSettings, byCustomer);
            }

            return GetMethodParametersForUniqueConstraint(table, codeWriterSettings, byCustomer);
        }

        public static IEnumerable<DatabaseColumn> GetPrimaryKeyColumns(DatabaseTable table)
        {
            return table.Columns.Where(c => c.IsPrimaryKey).ToList().OrderBy(item => item.Name);
        }

        public static IEnumerable<IEnumerable<DatabaseColumn>> GetUniqueConstraintColumns(DatabaseTable table)
        {
            var items = new List<List<DatabaseColumn>>();
            foreach (var uk in table.UniqueKeys)
            {
                var cols = new List<DatabaseColumn>();
                foreach (var c in uk.Columns)
                {
                    cols.Add(table.FindColumn(c));
                }

                items.Add(cols);
            }

            return items;
        }

        public static IEnumerable<DatabaseColumn> GetForeignKeyColumns(DatabaseTable table)
        {
            var c = new List<DatabaseColumn>();
            foreach (var fk in table.ForeignKeys)
            {
                if (fk.RefersToTable == fk.TableName)
                {
                    foreach (var rc in fk.ReferencedColumns(table.DatabaseSchema))
                    {
                        c.Add(table.FindColumn(rc));
                    }
                }
                else
                {
                    foreach (var _c in fk.Columns)
                    {
                        c.Add(table.FindColumn(_c));
                    }
                }
            }

            return c;
        }

        public static IEnumerable<DatabaseColumn> GetInverseForeignKeyReferencedColumns(DatabaseTable table)
        {
            var c = new List<DatabaseColumn>();
            foreach (var ifk in table.InverseForeignKeys(table))
            {
                if (ifk.RefersToTable != ifk.TableName)
                {
                    foreach (var rc in ifk.ReferencedColumns(table.DatabaseSchema))
                    {
                        c.Add(ifk.ReferencedTable(table.DatabaseSchema).FindColumn(rc));
                    }
                }
                else
                {
                    foreach (var _c in ifk.Columns)
                    {
                        c.Add(ifk.ReferencedTable(table.DatabaseSchema).FindColumn(_c));
                    }
                }
            }

            return c;
        }

        public static IEnumerable<Parameter> GetMethodParametersForUniqueConstraint(DatabaseTable table, CodeWriterSettings codeWriterSettings, bool byCustomer)
        {
            if (!table.UniqueKeys.Any())
            {
                return new List<Parameter>();
            }

            var columns = table.Columns.Where(item => item.IsUniqueKey).ToList().OrderBy(item => item.Name).ToList();
            var methodParameters = GetMethodParametersForColumns(columns, codeWriterSettings);
            if (byCustomer)
            {
                if (TableHasOrgUnitForeignKey(table))
                {
                    methodParameters.Add(GetCustomerParameter(table.DatabaseSchema, codeWriterSettings));
                }
                else
                {
                    return new List<Parameter>();
                }
            }

            return methodParameters;
        }

        public static IEnumerable<Parameter> GetMethodParametersForPrimaryKeys(DatabaseTable table, CodeWriterSettings codeWriterSettings, bool byCustomer)
        {
            var columns = table.Columns.Where(c => c.IsPrimaryKey).ToList().OrderBy(item => item.Name).ToList();
            var methodParameters = GetMethodParametersForColumns(columns, codeWriterSettings);
            if (byCustomer)
            {
                if (TableHasOrgUnitForeignKey(table))
                {
                    methodParameters.Add(GetCustomerParameter(table.DatabaseSchema, codeWriterSettings));
                }
                else
                {
                    return new List<Parameter>();
                }
            }

            return methodParameters;
        }

        private static bool TableHasOrgUnitForeignKey(DatabaseTable table)
        {
            return table.ForeignKeys.Any(fk => fk.Columns.Any(fkc => fkc.Equals(CustomerAssetOrganizationIDColumnName)) &&
                                               fk.ReferencedColumns(table.DatabaseSchema).Any(rfc => rfc.Equals(CustomerAssetOrganizationIDColumnName)));
        }

        public static string PrintParametersForSignature(IEnumerable<Parameter> methodParameters)
        {
            if (methodParameters?.Count() < 1)
            {
                return String.Empty;
            }

            return String.Join(", ", methodParameters.Select(mp => $"{mp.DataType} {mp.Name}"));
        }

        public static List<Parameter> GetMethodParametersForColumns(IEnumerable<DatabaseColumn> columns, CodeWriterSettings codeWriterSettings)
        {
            var methodParameters = new List<Parameter>();
            foreach (var column in columns.ToList().OrderBy(item => item.Name))
            {
                var ta = codeWriterSettings.Namer.NameToAcronym(column.TableName);
                var pn = codeWriterSettings.Namer.NameToAcronym(GetPropertyNameForDatabaseColumn(column));
                var dt = FindDataType(column);
                var cn = GetPropertyNameForDatabaseColumn(column);
                var fn = Regex.Replace(GetPropertyNameForDatabaseColumn(column), "([A-Z]+|[0-9]+)", " $1", RegexOptions.Compiled).Trim();
                var fields = fn.Split(' ').ToList();
                var firstChar = fields[0].ToLower()[0];
                if (firstChar == 'a' || firstChar == 'e' || firstChar == 'i' || firstChar == 'o' || firstChar == 'u')
                {
                    fields.Insert(0, "An");
                }
                else
                {
                    fields.Insert(0, "A");
                }

                for (var i = 1; i < fields.Count; i++)
                {
                    var f = fields[i];
                    if (f.ToLower() == "id")
                    {
                        fields[i] = "ID";
                        continue;
                    }

                    fields[i] = fields[i].ToLower();
                }

                var summary = String.Join(" ", fields) + ".";

                methodParameters.Add(new Parameter() { Name = pn, DataType = dt, ColumnNameToQueryBy = cn, Summary = summary, TableAlias = ta });
            }

            DeduplicateMethodParameterNames(methodParameters);
            return methodParameters;
        }

        public static string GetPropertyNameForDatabaseColumn(DatabaseColumn column)
        {
            var propertyName = column.Name;
            return propertyName;
        }

        public static string GetRepositoryInterfaceName(DatabaseTable table)
        {
            var entityName = $"{table.NetName}";
            if (entityName.EndsWith("Entity"))
            {
                entityName = entityName.Remove(entityName.Length - 6);
            }

            return $"I{entityName}Repository";
        }

        public static string GetRepositoryImplementationName(DatabaseTable table)
        {
            var interfaceName = GetRepositoryInterfaceName(table);
            if (interfaceName.StartsWith("I"))
            {
                interfaceName = interfaceName.Substring(1);
            }

            return interfaceName;
        }

        public static IEnumerable<Parameter> AddEntityParameter(IEnumerable<Parameter> parameters, DatabaseTable table, string parameterSummary)
        {
            var p = parameters.ToList();
            p.Add(GetEntityParameter(table, parameterSummary));
            return p;
        }

        private static Parameter GetEntityParameter(DatabaseTable table, string parameterSummary)
        {
            return new Parameter
            {
                Name = "entity",
                Summary = parameterSummary,
                DataType = table.NetName
            };
        }

        private static Parameter GetCustomerParameter(DatabaseSchema schema, CodeWriterSettings codeWriterSettings)
        {
            var orgUnitTable = schema.FindTableByName(CustomerAssetOrganizationTableName);
            var methodParameters = GetMethodParametersForColumns(new List<DatabaseColumn>
                                                                     {
                orgUnitTable.FindColumn(CustomerIDColumnName)
            }, codeWriterSettings);
            return methodParameters.Single();
        }

        public static string WriteClassFile(DirectoryInfo directory, string className, string txt)
        {
            var fileName = className + ".cs";
            var path = Path.Combine(directory.FullName, fileName);
            if (!directory.Exists) directory.Create();
            File.WriteAllText(path, txt);
            return fileName;
        }

        public static string FindDataType(DatabaseColumn column)
        {
            var dt = column.DataType;
            string dataType;
            if (dt == null)
            {
                dataType = "object";
            }
            else
            {
                //use precision and scale for more precise conversion
                dataType = dt.NetCodeName(column);
            }
            //if it's nullable (and not string or array)
            if (column.Nullable &&
                dt != null &&
                !dt.IsString &&
                !String.IsNullOrEmpty(dataType) &&
                !dataType.EndsWith("[]", StringComparison.OrdinalIgnoreCase) &&
                !dt.IsGeospatial)
            {
                dataType += "?"; //nullable
            }
            return dataType;
        }
    }
}
