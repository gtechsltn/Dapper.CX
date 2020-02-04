﻿using AO.DbSchema.Attributes.Interfaces;
using AO.DbSchema.Enums;
using AO.DbSchema.Interfaces;
using Dapper.CX.Classes;
using Dapper.CX.Exceptions;
using Dapper.CX.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Dapper.CX.Abstract
{
    public abstract class SqlCrudProvider<TIdentity>
    {
        protected abstract string SelectIdentityCommand { get; }
        protected abstract char StartDelimiter { get; }
        protected abstract char EndDelimiter { get; }

        /// <summary>
        /// Types supported by this handler when mapping to an object.
        /// </summary>
        protected abstract Type[] SupportedTypes { get; }

        protected abstract TIdentity ConvertIdentity(object identity);

        public TIdentity GetIdentity<TModel>(TModel model)
        {
            var idProperty = typeof(TModel).GetIdentityProperty();
            object idValue = idProperty.GetValue(model);
            return ConvertIdentity(idValue);
        }

        public bool IsNew<TModel>(TModel model)
        {
            return GetIdentity(model).Equals(default(TIdentity));
        }

        public async Task<TModel> GetAsync<TModel>(IDbConnection connection, TIdentity identity)
        {
            var result = await connection.QuerySingleOrDefaultAsync<TModel>(GetQuerySingleStatement(typeof(TModel)), new { id = identity });

            await OnGetRelatedAsync(connection, result);

            return result;
        }

        public async Task<TModel> GetWhereAsync<TModel>(IDbConnection connection, object criteria)
        {
            var result = await connection.QuerySingleOrDefaultAsync<TModel>(GetQuerySingleWhereStatement(typeof(TModel), criteria), criteria);

            await OnGetRelatedAsync(connection, result);

            return result;
        }

        private static async Task OnGetRelatedAsync<TModel>(IDbConnection connection, TModel result)
        {
            if (result == null) return;

            if (typeof(TModel).Implements(typeof(IGetRelated)))
            {
                await ((IGetRelated)result).OnGetAsync.Invoke(connection);
            }
        }

        public async Task<TIdentity> SaveAsync<TModel>(IDbConnection connection, TModel model, ChangeTracker<TModel> changeTracker = null, Action<TModel, SaveAction> onSave = null)
        {
            if (IsNew(model))
            {
                return await InsertAsync(connection, model, onSave);
            }
            else
            {                
                await UpdateAsync(connection, model, changeTracker, onSave);
                return GetIdentity(model);
            }
        }

        public async Task<TIdentity> MergeAsync<TModel>(IDbConnection connection, TModel model, IEnumerable<string> keyProperties, ChangeTracker<TModel> changeTracker = null, Action<TModel, SaveAction> onSave = null)
        {
            if (IsNew(model))
            {
                var existing = await GetByPropertiesAsync(connection, model, keyProperties);
                if (existing != null) SetIdentity(model, GetIdentity(existing));
            }

            return await SaveAsync(connection, model, changeTracker, onSave);
        }

        public async Task<TIdentity> MergeAsync<TModel>(IDbConnection connection, TModel model, ChangeTracker<TModel> changeTracker = null, Action<TModel, SaveAction> onSave = null)
        {
            var props = typeof(TModel).GetProperties().Where(pi => pi.HasAttribute<KeyAttribute>()).Select(pi => pi.GetColumnName());
            if (!props.Any()) throw new Exception($"No primary key properties found on {typeof(TModel).Name}");

            return await MergeAsync(connection, model, props, changeTracker, onSave);
        }

        private void SetIdentity<TModel>(TModel model, TIdentity identity)
        {
            if (IsNew(model))
            {
                var identityProp = typeof(TModel).GetIdentityProperty();
                identityProp.SetValue(model, identity);
            }
            else
            {
                throw new InvalidOperationException("Can't set a record's identity more than once.");
            }
        }

        private async Task<TModel> GetByPropertiesAsync<TModel>(IDbConnection connection, TModel model, IEnumerable<string> properties)
        {
            string sql = GetQuerySingleWhereStatement(typeof(TModel), properties);
            return await connection.QuerySingleOrDefaultAsync<TModel>(sql, model);
        }

        public async Task<TIdentity> InsertAsync<TModel>(IDbConnection connection, TModel model, Action<TModel, SaveAction> onSave = null)
        {
            await ValidateInternal(connection, model);

            onSave?.Invoke(model, SaveAction.Insert);
            var cmd = new CommandDefinition(GetInsertStatement(typeof(TModel)), model);

            try
            {
                TIdentity result = await connection.QuerySingleOrDefaultAsync<TIdentity>(cmd);
                SetIdentity(model, result);
                return result;
            }
            catch (Exception exc)
            {
                throw new CrudException(cmd, exc);
            }
        }

        public async Task UpdateAsync<TModel>(IDbConnection connection, TModel model, ChangeTracker<TModel> changeTracker = null, Action<TModel, SaveAction> onSave = null)
        {
            await ValidateInternal(connection, model);

            onSave?.Invoke(model, SaveAction.Update);
            var cmd = new CommandDefinition(GetUpdateStatement(model, changeTracker), model);

            try
            {                               
                await connection.ExecuteAsync(cmd);
            }
            catch (Exception exc)
            {
                throw new CrudException(cmd, exc);
            }
        }

        public async Task DeleteAsync<TModel>(IDbConnection connection, TIdentity id)
        {
            var cmd = new CommandDefinition(GetDeleteStatement(typeof(TModel)), new { id });

            try
            {
                await connection.ExecuteAsync(cmd);
            }
            catch (Exception exc)
            {
                throw new CrudException(cmd, exc);
            }
        }

        public async Task<bool> ExistsAsync<TModel>(IDbConnection connection, TIdentity id)
        {
            var model = await GetAsync<TModel>(connection, id);
            return (model != null);
        }

        public async Task<bool> ExistsWhereAsync<TModel>(IDbConnection connection, object criteria)
        {
            var model = await GetWhereAsync<TModel>(connection, criteria);
            return (model != null);
        }

        private static async Task ValidateInternal<TModel>(IDbConnection connection, TModel model)
        {
            if (typeof(TModel).Implements(typeof(IValidate)))
            {
                var result = ((IValidate)model).Validate();
                if (!result.IsValid) throw new Exceptions.ValidationException(result.Message);

                result = await ((IValidate)model).ValidateAsync(connection);
                if (!result.IsValid) throw new Exceptions.ValidationException(result.Message);
            }
        }

        #region SQL statements
        public string GetQuerySingleStatement(Type modelType)
        {
            bool isCustom = modelType.Implements(typeof(ICustomGet));

            string query = (isCustom) ?
                GetCustomSelectFrom(modelType) :
                $"SELECT * FROM {ApplyDelimiter(modelType.GetTableName())}";

            string whereId = (isCustom) ?
                GetCustomWhereId(modelType) :
                $"{ApplyDelimiter(modelType.GetIdentityName())}=@id";

            return $"{query} WHERE {whereId}";
        }

        public string GetQuerySingleWhereStatement(Type modelType, object criteria)
        {
            var properties = criteria.GetType().GetProperties();
            return GetQuerySingleWhereStatement(modelType, properties);
        }

        public string GetQuerySingleWhereStatement(Type modelType, IEnumerable<string> propertyNames)
        {
            string whereClause = $"WHERE {string.Join(" AND ", propertyNames.Select(name => ApplyDelimiter(name) + "=@" + name))}";

            string query = (modelType.Implements(typeof(ICustomGet))) ? 
                GetCustomSelectFrom(modelType) :
                $"SELECT * FROM {ApplyDelimiter(modelType.GetTableName())}";

            return $"{query} {whereClause}";
        }

        private string GetCustomSelectFrom(Type modelType)
        {
            var model = Activator.CreateInstance(modelType) as ICustomGet;
            return model.SelectFrom;
        }

        private string GetCustomWhereId(Type modelType)
        {
            var model = Activator.CreateInstance(modelType) as ICustomGet;
            return model.WhereId;
        }

        public string GetQuerySingleWhereStatement(Type modelType, IEnumerable<PropertyInfo> properties)
        {
            return GetQuerySingleWhereStatement(modelType, properties.Select(pi => pi.GetColumnName()));            
        }

        public string GetInsertStatement(Type modelType)
        {
            var columns = GetMappedProperties(modelType, SaveAction.Insert).Select(pi => pi.GetColumnName());

            return
                $@"INSERT INTO {ApplyDelimiter(modelType.GetTableName())} (
                    {string.Join(", ", columns.Select(col => ApplyDelimiter(col)))}
                ) VALUES (
                    {string.Join(", ", columns.Select(col => "@" + col))}
                ); " + SelectIdentityCommand;
        }

        public string GetUpdateStatement<TModel>(TModel model, ChangeTracker<TModel> changeTracker = null)
        {
            var columns = 
                changeTracker?.GetModifiedColumns(SaveAction.Update) ?? 
                GetMappedProperties(typeof(TModel), SaveAction.Update).Select(pi => pi.GetColumnName());

            var type = typeof(TModel);
            string identityCol = type.GetIdentityName();

            return 
                $@"UPDATE {ApplyDelimiter(type.GetTableName())} SET 
                    {string.Join(", ", columns.Select(col => $"{ApplyDelimiter(col)}=@{col}"))} 
                WHERE 
                    {ApplyDelimiter(identityCol)}=@{identityCol}";
        }

        private PropertyInfo[] GetMappedProperties(Type modelType, SaveAction saveAction)
        {
            bool isMapped(PropertyInfo pi)
            {
                if (!pi.CanWrite) return false;
                if (pi.IsIdentity()) return false;                
                if (!SupportedTypes.Contains(pi.PropertyType)) return false;
                if (!pi.AllowSaveAction(saveAction)) return false;

                var attr = pi.GetCustomAttribute<NotMappedAttribute>();
                if (attr != null) return false;

                return true;
            };

            return modelType.GetProperties().Where(pi => isMapped(pi)).ToArray();
        }

        public string GetDeleteStatement(Type modelType)
        {                        
            return $@"DELETE {ApplyDelimiter(modelType.GetTableName())} WHERE {ApplyDelimiter(modelType.GetIdentityName())}=@id";
        }

        protected string ApplyDelimiter(string name)
        {
            return string.Join(".", name
                .Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(namePart => $"{StartDelimiter}{namePart}{EndDelimiter}"));
        }

        #endregion
    }
}