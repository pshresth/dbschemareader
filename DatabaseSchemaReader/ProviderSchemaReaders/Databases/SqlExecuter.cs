﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace DatabaseSchemaReader.ProviderSchemaReaders.Databases
{

    abstract class SqlExecuter<T> : SqlExecuter where T : new()
    {

        protected List<T> Result { get; } = new List<T>();
    }

    abstract class SqlExecuter
    {

        public string Sql { get; set; }

        public string Owner { get; set; }

        protected void ExecuteDbReader(DbConnection connection, DbTransaction transaction)
        {
            if (connection.State == ConnectionState.Closed)
            {
                connection.Open();
            }
            Trace.WriteLine($"Sql: {Sql}");
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = Sql;
                AddParameters(cmd);
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        Mapper(dr);
                    }
                }
            }
        }

        protected DbCommand BuildCommand(DbConnection connection, DbTransaction transaction)
        {
            var cmd =  connection.CreateCommand();
            if (transaction != null)
            {
                cmd.Transaction = transaction;
            }
            return cmd;
        }

        protected static DbParameter AddDbParameter(DbCommand command, string parameterName, object value, DbType? dbType = null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = value ?? DBNull.Value;
            if (dbType.HasValue) parameter.DbType = dbType.Value; //SqlServerCe needs this
            command.Parameters.Add(parameter);
            return parameter;
        }

        protected abstract void AddParameters(DbCommand command);

        protected abstract void Mapper(IDataRecord record);
    }
}
