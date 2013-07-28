﻿using System;
using System.Collections.Generic;
using System.Linq;
using DatabaseSchemaReader.DataSchema;

namespace DatabaseSchemaReader.Compare
{
    class CompareSequences
    {
        private readonly IList<CompareResult> _results;
        private readonly ComparisonWriter _writer;

        public CompareSequences(IList<CompareResult> results, ComparisonWriter writer)
        {
            _results = results;
            _writer = writer;
        }

        public void Execute(IEnumerable<DatabaseSequence> baseSequences, IEnumerable<DatabaseSequence> compareSequences)
        {
            //find new sequences (in compare, but not in base)
            foreach (var sequence in compareSequences)
            {
                var name = sequence.Name;
                var schema = sequence.SchemaOwner;
                var match = baseSequences.FirstOrDefault(t => t.Name == name && t.SchemaOwner == schema);
                if (match != null) continue;
                CreateResult(ResultType.Add, name, "-- NEW SEQUENCE " + sequence.Name + Environment.NewLine +
                    _writer.AddSequence(sequence));
            }

            //find dropped and existing sequence
            foreach (var sequence in baseSequences)
            {
                var name = sequence.Name;
                var schema = sequence.SchemaOwner;
                var match = compareSequences.FirstOrDefault(t => t.Name == name && t.SchemaOwner == schema);
                if (match == null)
                {
                    CreateResult(ResultType.Delete, name, "-- DROP SEQUENCE " + sequence.Name + Environment.NewLine +
                       _writer.DropSequence(sequence));
                }

                //we could alter the sequence, but it's rare you'd ever want to do this
            }
        }


        private void CreateResult(ResultType resultType, string name, string script)
        {
            var result = new CompareResult
                {
                    SchemaObjectType = SchemaObjectType.Sequence,
                    ResultType = resultType,
                    Name = name,
                    Script = script
                };
            _results.Add(result);
        }
    }
}
