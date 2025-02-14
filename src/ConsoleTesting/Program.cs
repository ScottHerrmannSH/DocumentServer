﻿using System.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.Reflection;
using DocumentServer.Core;
using Microsoft.Extensions.Configuration;
using ConsoleTesting;
using DocumentServer.Db;
using Microsoft.Extensions.Logging;
using ILogger = Serilog.ILogger;
using Microsoft.EntityFrameworkCore;



namespace SlugEnt.DocumentServer.ConsoleTesting;

public class Program
{
    private static ILogger        _logger;
    private static IConfiguration _configuration;


    public static async Task Main(string[] args)
    {
#if DEBUG
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug()
                                              .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
#else
			Log._logger = new LoggerConfiguration().MinimumLevel.Information()
			                                      .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
#endif
                                              .Enrich.FromLogContext()
                                              .WriteTo.Console()
                                              .CreateLogger();
        _logger = Log.Logger;
        Log.Information("Starting {AppName}", Assembly.GetExecutingAssembly().GetName().Name);


        // Get Sensitive Appsettings.json file location
        string sensitiveAppSettings = Environment.GetEnvironmentVariable("AppSettingSensitiveFolder");


        // 1.B.  We keep the AppSettings file in the root App folder on the servers so it never gets overwritten
        string        versionPath          = Directory.GetCurrentDirectory();
        DirectoryInfo appRootDirectoryInfo = Directory.GetParent(versionPath);
        string        appRoot              = appRootDirectoryInfo.FullName;
        Console.WriteLine("Running from Directory:  " + appRoot);

        // Load Environment Specific App Setting file
        string environmentName    = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        string appSettingFileName = $"appsettings." + environmentName + ".json";
        string appSettingEnvFile  = Path.Join(appRoot, appSettingFileName);
        DisplayAppSettingStatus(appSettingEnvFile);


        // Load the Sensitive AppSettings.JSON file.
        string sensitiveFileName    = Assembly.GetExecutingAssembly().GetName().Name + "_AppSettingsSensitive.json";
        string sensitiveSettingFile = Path.Join(sensitiveAppSettings, sensitiveFileName);
        DisplayAppSettingStatus(sensitiveSettingFile);


        // Add our custom AppSettings.JSON files
        IConfigurationRoot configuration = new ConfigurationBuilder().AddJsonFile(appSettingEnvFile, true, true)
                                                                     .AddJsonFile(sensitiveSettingFile, true, true).Build();


        using IHost host = Host.CreateDefaultBuilder(args)

                               // Add our custom config from above to the default configuration
                               .ConfigureAppConfiguration(config => { config.AddConfiguration(configuration); })
                               .UseSerilog()
                               .ConfigureServices((_, services) =>

                                                      // The main program     
                                                      services
                                                          .AddDbContext<DocServerDbContext>(options =>
                                                          {
                                                              options
                                                                  .UseSqlServer(configuration.GetConnectionString(DocServerDbContext.DatabaseReferenceName()))

                                                                  // IF Debug then log all SQL to Console
#if (DEBUG || SWAGGER)
                                                                  .LogTo(Console.WriteLine, LogLevel.Debug)
                                                                  .EnableDetailedErrors();

#endif
                                                              ;
                                                          })
                                                          .AddTransient<MainMenu>()
                                                          .AddTransient<DocumentServerEngine>()
                                                          .AddHttpClient<AccessDocumentServerHttpClient>().ConfigurePrimaryHttpMessageHandler(() =>
                                                          {
                                                              return new SocketsHttpHandler()
                                                              {
                                                                  PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                                                              };
                                                          })
                                                          .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
                                                 )
                               .Build();


        // Run the Main App. Do NOT AWAIT call
#pragma warning disable CS4014
        host.RunAsync();
        MainMenu mainMenu = host.Services.GetRequiredService<MainMenu>();
        await mainMenu.Start();
        Log.CloseAndFlush();
#pragma warning restore
    }


    /// <summary>
    /// Logs whether a given AppSettings file was found to exist.
    /// </summary>
    /// <param name="appSettingFileName"></param>
    private static void DisplayAppSettingStatus(string appSettingFileName)
    {
        if (File.Exists(appSettingFileName))
            _logger.Information("AppSettings File was located.  {AppSettingsFile}", appSettingFileName);
        else
            _logger.Warning("AppSettings File was not found.  {AppSettingsFile}", appSettingFileName);
    }
}