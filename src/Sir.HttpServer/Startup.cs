﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sir.HttpServer.Features;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Sir.HttpServer
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        public IServiceProvider ServiceProvider { get; private set; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc(options =>
            {
                options.RespectBrowserAcceptHeader = true;
            });

            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");

            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            ServiceProvider = ServiceConfiguration.Configure(services);
        }

        public void Configure(IApplicationBuilder app, IApplicationLifetime applicationLifetime, IHostingEnvironment env)
        {
            app.UseMiddleware<ErrorLoggingMiddleware>();

            applicationLifetime.ApplicationStopping.Register(OnShutdown);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMvc(routes =>
            {
                routes.MapRoute("default", "{controller}/{action}", new { controller = "Home", action = "Index" });
            });

            app.UseStaticFiles();
        }

        private void OnShutdown()
        {
            var crawlQueue = ServiceProvider.GetService<CrawlQueue>();
            crawlQueue.Dispose();

            var plugins = ServiceProvider.GetService<PluginsCollection>();
            plugins.Dispose();

            foreach (var stopper in ServiceProvider.GetServices<IPluginStop>())
            {
                stopper.OnApplicationShutdown(ServiceProvider);
            }
        }
    }

    public class ErrorLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        public ErrorLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception e)
            {
                File.AppendAllText("sir.httpserver.log.txt", e.ToString());
                throw;
            }
        }
    }
}
