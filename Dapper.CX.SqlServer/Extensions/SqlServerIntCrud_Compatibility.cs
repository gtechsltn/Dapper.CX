﻿using System;
using System.Data;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Dapper.CX.SqlServer.Extensions.Int
{
    public static partial class SqlServerIntCrud
    {
        public static async Task<int> SaveAsync<TModel>(IDbConnection connection, TModel @object, params string[] columnNames)
        {
            var provider = new SqlServerIntCrudProvider();
            return await provider.SaveAsync(connection, @object, columnNames);
        }

        public static async Task UpdateAsync<TModel>(IDbConnection connection, TModel @object, params Expression<Func<TModel, object>>[] setColumns)
        {
            var provider = new SqlServerIntCrudProvider();
            await provider.UpdateAsync(connection, @object, setColumns);
        }
    }
}