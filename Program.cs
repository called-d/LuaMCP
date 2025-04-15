
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LuaMCP {

    class Program
    {
        static async Task Main(string[] args)
        {
            var luaEngine = new LuaEngine();
            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.AddConsole(consoleLogOptions =>
            {
                // Configure all logs to go to stderr
                consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
            });
            builder.Services
                .AddSingleton(luaEngine)
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();
            var server = builder.Build();

            await server.RunAsync();
        }
    }

    [McpServerToolType]
    public static class EvalTool
    {
        [McpServerTool, Description("eval lua code and return results as json")]
        public static async Task<string> EvalLuaCode(IMcpServer server, IServiceProvider services, string code)
        {
            await server.SendNotificationAsync("eval", new {
                Code = code,
            });
            var luaEngine = services.GetRequiredService<LuaEngine>();
            LuaEngine.onPrint = async (args) => {
                var arr = new JsonArray();
                foreach (var arg in args) arr.Add(arg);
                await server.SendNotificationAsync("print", new {
                    Args = JsonSerializer.Serialize(arr),
                });
            };
            try {
                return luaEngine.Call(code, []);
            } catch (Exception ex) {
                Console.Error.WriteLine(ex.ToString());
                await server.SendNotificationAsync("error", new {
                    ex.Message,
                });
                return JsonSerializer.Serialize(new JsonArray([null, ex.Message]));
            }
        }
        [McpServerTool(Destructive = false, ReadOnly = true), Description("get global variable list")]
        public static string[] ListGlobals(IServiceProvider services, [Description("not filter libraries and misc")] bool noFilter = false)
        {
            var luaEngine = services.GetRequiredService<LuaEngine>();
            return luaEngine.GetListGlobals(!noFilter);
        }
    }
}
