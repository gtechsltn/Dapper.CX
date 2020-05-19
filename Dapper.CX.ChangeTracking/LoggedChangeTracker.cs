﻿using AO.Models.Interfaces;
using Dapper.CX.ChangeTracking.Models;
using Dapper.CX.Extensions;
using Dapper.CX.SqlServer.Extensions.Long;
using ModelSync.Library.Models;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Dapper.CX.Classes
{
    public class LoggedChangeTracker<TModel> : ChangeTracker<TModel>, IDbSaveable
    {
        private static bool _initialized = false;

        private readonly IUserBase _user;
        private readonly string _nullText;

        public LoggedChangeTracker(IUserBase user, TModel @object, string nullText = "<null>") : base(@object)
        {
            _user = user;
            _nullText = nullText;
        }

        private enum ValueType
        {
            Enum,
            Lookup,
            Raw
        }

        public async Task SaveAsync(IDbConnection connection)
        {
            await InitializeAsync(connection);

            string tableName = typeof(TModel).GetTableName();
            long rowId = GetRowId();

            using (var txn = connection.BeginTransaction())
            {
                try
                {
                    int version = await IncrementRowVersionAsync(connection, tableName, rowId, txn);

                    var textLookup = Instance as AO.Models.Interfaces.ITextLookup;

                    foreach (var kp in GetModifiedProperties())
                    {
                        var rawOldValue = this[kp.Key];
                        var rawNewValue = kp.Value.GetValue(Instance);

                        var valueType = 
                            (kp.Value.PropertyType.IsEnum) ? ValueType.Enum :
                            (textLookup?.GetLookupProperties()?.Contains(kp.Key) ?? false) ? ValueType.Lookup :
                            ValueType.Raw;
                        
                        var oldValue = 
                            (valueType == ValueType.Enum) ? rawOldValue?.ToString() :
                            (valueType == ValueType.Lookup) ? await textLookup.GetTextFromKeyAsync(connection, txn, kp.Key, rawOldValue) :
                            rawOldValue;

                        var newValue =
                            (valueType == ValueType.Enum) ? rawNewValue?.ToString() :
                            (valueType == ValueType.Lookup) ? await textLookup.GetTextFromKeyAsync(connection, txn, kp.Key, rawNewValue) :
                            rawNewValue;

                        var history = new ColumnHistory()
                        {
                            UserName = _user.Name,
                            Timestamp = _user.LocalTime,
                            TableName = tableName,
                            RowId = rowId,
                            Version = version,
                            ColumnName = kp.Key,
                            OldValue = oldValue?.ToString() ?? _nullText,
                            NewValue = newValue?.ToString() ?? _nullText
                        };

                        await connection.SaveAsync(history, txn: txn);
                    }

                    txn.Commit();
                }
                catch 
                {
                    txn.Rollback();
                    throw;
                }
            }            
        }

        private async Task<int> IncrementRowVersionAsync(IDbConnection connection, string tableName, long rowId, IDbTransaction txn)
        {
            var rowVersion = await connection.GetWhereAsync<RowVersion>(new { tableName, rowId }, txn) ?? new RowVersion()
            {
                TableName = tableName,
                RowId = rowId                
            };

            rowVersion.Version++;

            await connection.SaveAsync(rowVersion, txn: txn);

            return rowVersion.Version;
        }

        private long GetRowId()
        {
            var idProperty = typeof(TModel).GetIdentityProperty();
            var value = idProperty.GetValue(Instance);
            return Convert.ToInt64(value);
        }

        private async Task InitializeAsync(IDbConnection connection)
        {
            if (_initialized) return;

            await DataModel.CreateTablesAsync(new Type[]
            {
                typeof(ColumnHistory),
                typeof(RowVersion)
            }, connection);

            _initialized = true;
        }
    }
}