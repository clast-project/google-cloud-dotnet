// Copyright 2016 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Google.Api.Gax;
using Google.Apis.Bigquery.v2.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Google.Cloud.BigQuery.V2
{
    /// <summary>
    /// A row in a result set, which may be from a query or from listing the rows in a table.
    /// </summary>
    public sealed class BigQueryRow
    {
        /// <summary>
        /// The underlying REST-ful resource for the row.
        /// </summary>
        public TableRow RawRow { get; }

        /// <summary>
        /// The schema to use when interpreting the row results.
        /// </summary>
        public TableSchema Schema { get; }

        /// <summary>
        /// The query ID, if this row is returned from a stateless query via jobs.query otherwise null.
        /// </summary>
        public string QueryId { get; }

        /// <summary>
        /// How to interpret timestamps: when this is true, timestamps are parsed
        /// as "integer number of microseconds since the Unix epoch"; when this is false,
        /// timestamps are parsed as "floating point number of seconds since the Unix epoch".
        /// </summary>
        private readonly bool _useInt64Timestamp;

        private readonly IDictionary<string, int> _fieldNameIndexMap;

        /// <summary>
        /// Constructs a row from the underlying REST-ful resource and schema.
        /// </summary>
        /// <remarks>
        /// This is public to allow tests to construct instances for production code to consume;
        /// production code should not normally construct instances itself. This constructor does not
        /// currently allow customization based on whether timestamps are represented using Int64
        /// values, and always assumes floating point values.
        /// </remarks>
        /// <param name="rawRow">The underlying REST-ful row resource. Must not be null.</param>
        /// <param name="schema">The table schema. Must not be null.</param>
        public BigQueryRow(TableRow rawRow, TableSchema schema) : this(rawRow, schema, schema?.IndexFieldNames(), false, null)
        {
        }

        internal BigQueryRow(TableRow rawRow, TableSchema schema, IDictionary<string, int> fieldNameIndexMap, bool useInt64Timestamp, string queryId = null)
        {
            RawRow = GaxPreconditions.CheckNotNull(rawRow, nameof(rawRow));
            Schema = GaxPreconditions.CheckNotNull(schema, nameof(schema));
            QueryId = queryId;
            _fieldNameIndexMap = fieldNameIndexMap;
            _useInt64Timestamp = useInt64Timestamp;
        }

        private static readonly Func<string, string> StringConverter = v => v;
        private static readonly Func<string, long> Int64Converter = v => long.Parse(v, CultureInfo.InvariantCulture);
        private static readonly Func<string, double> DoubleConverter = v => double.Parse(v, CultureInfo.InvariantCulture);

        // AddSeconds rounds to the nearest millisecond, for some reason.
        // Instead, we work out the number of ticks and add that.
        private static readonly Func<string, DateTime> DoubleTimestampConverter = v =>
        {
            decimal seconds = decimal.Parse(v, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            long microseconds = (long) (seconds * 1e6m);
            long ticks = microseconds * 10;
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ticks);
        };

        private static readonly Func<string, DateTime> Int64TimestampConverter = v =>
        {
            long microseconds = long.Parse(v, CultureInfo.InvariantCulture);
            long ticks = microseconds * 10;
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ticks);
        };

        private static readonly Func<string, DateTime> DateConverter = v => DateTime.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static readonly Func<string, TimeSpan> TimeConverter = v => DateTime.ParseExact(v, "HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture).TimeOfDay;
        private static readonly Func<string, DateTime> DateTimeConverter = v => DateTime.ParseExact(v, "yyyy-MM-dd'T'HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture);
        private static readonly Func<string, byte[]> BytesConverter = v => Convert.FromBase64String(v);
        private static readonly Func<string, bool> BooleanConverter = v => v == "true";
        private static readonly Func<string, BigQueryNumeric> NumericConverter = BigQueryNumeric.Parse;
        private static readonly Func<string, BigQueryBigNumeric> BigNumericConverter = BigQueryBigNumeric.Parse;
        private static readonly Func<string, BigQueryGeography> GeographyConverter = BigQueryGeography.Parse;

        /// <summary>
        /// Retrieves a cell value by field name.
        /// </summary>
        public object this[string name] => this[_fieldNameIndexMap[name]];

        /// <summary>
        /// Retrieves a cell value by index.
        /// </summary>
        public object this[int index]
        {
            get
            {
                object rawValue = RawRow.F[index].V;
                var field = Schema.Fields[index];

                return ConvertSingleValue(rawValue, field, _useInt64Timestamp);
            }
        }

        private static object ConvertSingleValue(object rawValue, TableFieldSchema field, bool useInt64Timestamp)
        {
            if (rawValue == null || (rawValue is JsonElement nullCheck && nullCheck.ValueKind == JsonValueKind.Null))
            {
                return null;
            }
            var type = field.GetFieldType();

            if (field.GetFieldMode() == BigQueryFieldMode.Repeated)
            {
                JsonElement array = (JsonElement) rawValue;
                return type switch
                {
                    BigQueryDbType.String or BigQueryDbType.Json => ConvertArray(array, StringConverter),
                    BigQueryDbType.Int64 => ConvertArray(array, Int64Converter),
                    BigQueryDbType.Float64 => ConvertArray(array, DoubleConverter),
                    BigQueryDbType.Bytes => ConvertArray(array, BytesConverter),
                    BigQueryDbType.Bool => ConvertArray(array, BooleanConverter),
                    BigQueryDbType.Timestamp => ConvertArray(array, useInt64Timestamp ? Int64TimestampConverter : DoubleTimestampConverter),
                    BigQueryDbType.Date => ConvertArray(array, DateConverter),
                    BigQueryDbType.Time => ConvertArray(array, TimeConverter),
                    BigQueryDbType.DateTime => ConvertArray(array, DateTimeConverter),
                    BigQueryDbType.Struct => ConvertRecordArray(array, field, useInt64Timestamp),
                    BigQueryDbType.Numeric => ConvertArray(array, NumericConverter),
                    BigQueryDbType.BigNumeric => ConvertArray(array, BigNumericConverter),
                    BigQueryDbType.Geography => ConvertArray(array, GeographyConverter),
                    _ => throw new InvalidOperationException($"Unhandled field type {type} {rawValue.GetType()}"),
                };
            }
            return type switch
            {
                BigQueryDbType.String or BigQueryDbType.Json => StringConverter(GetRawString(rawValue)),
                BigQueryDbType.Int64 => Int64Converter(GetRawString(rawValue)),
                BigQueryDbType.Float64 => DoubleConverter(GetRawString(rawValue)),
                BigQueryDbType.Bytes => BytesConverter(GetRawString(rawValue)),
                BigQueryDbType.Bool => BooleanConverter(GetRawString(rawValue)),
                BigQueryDbType.Timestamp => (useInt64Timestamp ? Int64TimestampConverter : DoubleTimestampConverter)(GetRawString(rawValue)),
                BigQueryDbType.Date => DateConverter(GetRawString(rawValue)),
                BigQueryDbType.Time => TimeConverter(GetRawString(rawValue)),
                BigQueryDbType.DateTime => DateTimeConverter(GetRawString(rawValue)),
                BigQueryDbType.Numeric => NumericConverter(GetRawString(rawValue)),
                BigQueryDbType.BigNumeric => BigNumericConverter(GetRawString(rawValue)),
                BigQueryDbType.Geography => GeographyConverter(GetRawString(rawValue)),
                BigQueryDbType.Struct => ConvertRecord((JsonElement) rawValue, field, useInt64Timestamp),
                _ => throw new InvalidOperationException($"Unhandled field type {type} (Underlying type: {rawValue.GetType()})"),
            };
        }

        // A scalar cell value arrives either as a CLR string (when extracted from a parent record, see
        // ConvertRecord) or as a JSON string element (when read directly from a deserialized response, where the
        // generated TableCell.V is typed `object`). Newtonsoft surfaced both cases as a string; System.Text.Json
        // represents the deserialized case as a JsonElement. See BEHAVIORAL-CHANGES.md BC-022 (an instance of BC-002).
        private static string GetRawString(object rawValue) =>
            rawValue is JsonElement element ? element.GetString() : (string) rawValue;

        // TODO: GetString etc, like IDataReader etc. (Should we actually implement IDataReader?)

        // Each element of a repeated cell is a JSON object of the form {"v": <scalar string>}. All callers pass a
        // converter over the string form, matching the original Newtonsoft behavior (which cast the JValue to string).
        private static T[] ConvertArray<T>(JsonElement array, Func<string, T> converter)
        {
            T[] ret = new T[array.GetArrayLength()];
            int i = 0;
            foreach (var element in array.EnumerateArray())
            {
                ret[i++] = converter(element.GetProperty("v").GetString());
            }
            return ret;
        }

        private static Dictionary<string, object>[] ConvertRecordArray(JsonElement array, TableFieldSchema fieldSchema, bool useInt64Timestamp)
        {
            var ret = new Dictionary<string, object>[array.GetArrayLength()];
            int i = 0;
            foreach (var element in array.EnumerateArray())
            {
                ret[i++] = ConvertRecord(element.GetProperty("v"), fieldSchema, useInt64Timestamp);
            }
            return ret;
        }

        private static Dictionary<string, object> ConvertRecord(JsonElement record, TableFieldSchema fieldSchema, bool useInt64Timestamp)
        {
            var fields = fieldSchema.Fields;
            JsonElement values = record.GetProperty("f");
            int valueCount = values.GetArrayLength();
            if (valueCount != fields.Count)
            {
                throw new InvalidOperationException($"Record had {valueCount} entries; expected {fields.Count}");
            }
            var ret = new Dictionary<string, object>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var token = values[i].GetProperty("v");
                // Surface scalar leaves as a plain string (as Newtonsoft did via the JToken->string cast), and
                // nested arrays/records as the JsonElement so the recursive call can re-interpret them.
                object value = token.ValueKind == JsonValueKind.String ? (object) token.GetString() : token;
                ret[field.Name] = ConvertSingleValue(value, field, useInt64Timestamp);
            }
            return ret;
        }
    }
}
