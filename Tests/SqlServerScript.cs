using Dapper.TinyCrud.SqlServer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Tests.Models;

namespace Tests
{
    [TestClass]
    public class SqlServerScript
    {
        [TestMethod]
        public void GreetingInsert()
        {
            var cmd = new SqlServerIntCmd("table1", "Id");
            cmd["Greeting"] = "Hello, World";
            cmd["CurrentTime"] = DateTime.Now;

            string insertCmd = cmd.GetInsertStatement();
            Assert.IsTrue(insertCmd.Equals(@"INSERT INTO [table1] (
                    [Greeting], [CurrentTime]
                ) VALUES (
                    @Greeting, @CurrentTime
                ); SELECT SCOPE_IDENTITY()"));
        }

        [TestMethod]
        public void GreetingUpdate()
        {
            var cmd = new SqlServerIntCmd("table1", "Id");
            cmd["Greeting"] = "Hello, World";
            cmd["CurrentTime"] = DateTime.Now;

            string updateCmd = cmd.GetUpdateStatement();
            Assert.IsTrue(updateCmd.Equals(@"UPDATE [table1] SET
                    [Greeting]=@Greeting, [CurrentTime]=@CurrentTime
                WHERE [Id]=@Id"));
        }

        [TestMethod]
        public void GreetingFromObject()
        {
            var g = new Greeting()
            {
                Message = "Hello, World",
                CurrentTime = DateTime.Now
            };

            var cmd = new SqlServerIntCmd(g);

            var insertCmd = cmd.GetInsertStatement();
            Assert.IsTrue(insertCmd.Equals(@"INSERT INTO [Greeting] (
                    [Message], [CurrentTime]
                ) VALUES (
                    @Message, @CurrentTime
                ); SELECT SCOPE_IDENTITY()"));

            var updateCmd = cmd.GetUpdateStatement();
            Assert.IsTrue(updateCmd.Equals(@"UPDATE [Greeting] SET
                    [Message]=@Message, [CurrentTime]=@CurrentTime
                WHERE [Id]=@Id"));
        }
    }
}
