﻿using AO.Models.Interfaces;
using Dapper.CX.Interfaces;
using Dapper.CX.Models;
using Dapper.CX.SqlServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace Dapper.CX.SqlServer.AspNetCore
{
    public static class CrudServiceExtensions
    {                
        /// <summary>
        /// Dapper.CX config method that gives user profile integration with local accounts
        /// </summary>
        public static void AddDapperCX<TIdentity, TUser>(
            this IServiceCollection services,
            string connectionString,
            Func<object, TIdentity> convertIdentity, 
            Func<DbUserClaimsConverter<TUser>> claimsConverterFactory)
            where TUser : IUserBase, new()            
        {
            services.AddHttpContextAccessor();
            services.AddSingleton(claimsConverterFactory.Invoke());            
            services.AddScoped((sp) =>
            {
                var context = sp.GetDapperCXContext<TUser>();
                return new DapperCX<TIdentity, TUser>(connectionString, context.user, convertIdentity)
                {
                    OnUserUpdatedAsync = async (user) =>
                    {
                        await context.claimsConverter.UpdateClaimsAsync(user.Name, context.userManager, context.signinManager, context.claims);
                    }
                };                
            });
        }

        public static void AddDapperCX<TIdentity, TUser>(
            this IServiceCollection services,
            string connectionString,
            Func<object, TIdentity> convertIdentity) where TUser : IUserBase
        {
            services.AddHttpContextAccessor();
            services.AddScoped((sp) =>
            {
                var http = sp.GetRequiredService<IHttpContextAccessor>();
                var userName = http.HttpContext.User.Identity.Name;
                var getUser = sp.GetRequiredService<IGetUser<TUser>>();
                var user = getUser.Get(userName);
                return new DapperCX<TIdentity, TUser>(connectionString, user, convertIdentity);
            });
        }

        /// <summary>
        /// simplest Dapper.CX use case, with no user profile integation
        /// </summary>
        public static void AddDapperCX<TIdentity>(
            this IServiceCollection services, 
            string connectionString, Func<object, TIdentity> convertIdentity)
        {
            services.AddScoped((sp) =>
            {                
                return new DapperCX<TIdentity>(connectionString, convertIdentity);
            });
        }        

        public static void AddSessionUser<TUser>(
            this IServiceCollection services,
            Func<ISession, IGetUser<TUser>> getUserFactory) where TUser : IUserBase
        {
            services.AddHttpContextAccessor();
            services.AddSession();
            services.AddScoped((sp) =>
            {
                var http = sp.GetRequiredService<IHttpContextAccessor>();
                return getUserFactory.Invoke(http.HttpContext.Session);
            });
        }

        public static void AddChangeTracking(this IServiceCollection services, string connectionString, ISqlObjectCreator objectCreator)
        {
            using (var cn = new SqlConnection(connectionString))
            {
                var commands = objectCreator.GetStatementsAsync(cn, new Type[]
                {
                    typeof(ColumnHistory),
                    typeof(RowVersion)
                }).Result;

                foreach (var cmd in commands) cn.Execute(cmd);
            }
        }

        /// <summary>
        /// Helper method that extracts the useful things from the service provider to configure Dapper.CX during startup.
        /// This is intended for use in your own serviceFactory method so you can implement user profile integration
        /// </summary>
        public static (
            TUser user,
            DbUserClaimsConverter<TUser> claimsConverter,
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signinManager,
            IEnumerable<Claim> claims
        ) GetDapperCXContext<TUser>(this IServiceProvider serviceProvider) where TUser : IUserBase, new()
        {
            var signinManager = serviceProvider.GetRequiredService<SignInManager<IdentityUser>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var claimsFactory = signinManager.ClaimsFactory as DbUserClaimsFactory<TUser>;
            var http = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            var claimsConverter = claimsFactory.ClaimsConverter;
            return (
                claimsConverter.GetUserFromClaims(http.HttpContext.User.Identity.Name, http.HttpContext.User.Claims),
                claimsConverter,
                userManager,
                signinManager,
                http.HttpContext.User.Claims
            );
        }
    }
}
