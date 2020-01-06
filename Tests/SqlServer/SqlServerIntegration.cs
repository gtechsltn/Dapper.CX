﻿using Dapper.CX.Abstract;
using Dapper.CX.Classes;
using Dapper.CX.SqlServer;
using Dapper.CX.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SqlServer.LocalDb;
using SqlServer.LocalDb.Models;
using System.Collections.Generic;
using System.Data;

namespace Tests.SqlServer
{
    [TestClass]
    public class SqlServerIntegrationInt : IntegrationBase<int>
    {
        [ClassInitialize]
        public static void Initialize(TestContext context)
        {
            LocalDb.TryDropDatabase("DapperCX", out _);

            using (var cn = LocalDb.GetConnection("DapperCX"))
            {
                LocalDb.ExecuteInitializeStatements(cn, CreateObjects());
            }            
        }

        protected override IDbConnection GetConnection()
        {
            return LocalDb.GetConnection("DapperCX");
        }

        private static IEnumerable<InitializeStatement> CreateObjects()
        {
            yield return new InitializeStatement(
                "dbo.Employee",
                "DROP TABLE %obj%",
                @"CREATE TABLE %obj% (
                    [FirstName] nvarchar(50) NOT NULL,
                    [LastName] nvarchar(50) NOT NULL,
                    [HireDate] date NULL,
                    [TermDate] date NULL,
                    [IsExempt] bit NOT NULL,
                    [Timestamp] datetime NULL,
                    [Id] int identity(1, 1) PRIMARY KEY
                )");
        }

        protected override SqlCrudProvider<int> GetProvider()
        {
            return new SqlServerIntCrudProvider();
        }

        [TestMethod]
        public void NewObjShouldBeNew()
        {
            NewObjShouldBeNewBase();
        }

        [TestMethod]
        public void Insert()
        {
            InsertBase();
        }

        [TestMethod]
        public void Update()
        {
            UpdateBase();
        }

        [TestMethod]
        public void Delete()
        {
            DeleteBase();
        }

        [TestMethod]
        public void Exists()
        {
            ExistsBase();
        }

        [TestMethod]
        public void ExistsWhere()
        {
            ExistsWhereBase();
        }

        [TestMethod]
        public void MergeExplicitProps()
        {
            MergeExplicitPropsBase();
        }

        [TestMethod]
        public void MergePKProps()
        {
            MergePKPropsBase();
        }

        [TestMethod]
        public void CmdDictionaryInsert()
        {            
            using (var cn = GetConnection())
            {
                var cmd = SqlServerCmd.FromTableSchemaAsync(cn, "dbo", "Employee").Result;
                cmd["FirstName"] = "Wilbur";
                cmd["LastName"] = "Wainright";
                cmd["IsExempt"] = true;
                cmd["Timestamp"] = new SqlExpression("getdate()");

                var sql = cmd.GetInsertStatement();
                var id = cmd.InsertAsync<int>(cn).Result;
                Assert.IsTrue(cn.RowExistsAsync("[dbo].[Employee] WHERE [LastName]='Wainright'").Result);
            }            
        }

        [TestMethod]
        public void CmdDictionaryUpdate()
        {
            // create our sample row
            CmdDictionaryInsert();

            using (var cn = GetConnection())
            {
                var cmd = SqlServerCmd.FromTableSchemaAsync(cn, "dbo", "Employee").Result;
                cmd["FirstName"] = "Wilbur";
                cmd["LastName"] = "Wainright2";
                cmd["IsExempt"] = true;
                cmd["Timestamp"] = new SqlExpression("getdate()");
                
                cmd.UpdateAsync(cn, 1).Wait();
                Assert.IsTrue(cn.RowExistsAsync("[dbo].[Employee] WHERE [LastName]='Wainright2'").Result);
            }
        }
    }
}
