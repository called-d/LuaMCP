
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LuaMCP {

    class Program
    {
        static async Task Main(string[] args)
        {
            var pool = new VMPool();
            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.AddConsole(consoleLogOptions =>
            {
                // Configure all logs to go to stderr
                consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
            });
            builder.Services
                .AddSingleton(pool)
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
        public static async Task<CallToolResponse> EvalLuaCode(
            IMcpServer server,
            IServiceProvider services,
            [Description("session id to specify lua environment. To create new environment, keep it empty")] string? sessionId,
            [Description("lua source code to eval")] string code
        )
        {
            await server.SendNotificationAsync("eval", new {
                Code = code,
            });
            var luaEngine = services.GetRequiredService<VMPool>().GetOrCreate(sessionId, out var sessionId_);
            LuaEngine.onPrint = async (args) => {
                var arr = new JsonArray();
                foreach (var arg in args) arr.Add(arg);
                await server.SendNotificationAsync("print", new {
                    Args = JsonSerializer.Serialize(arr),
                });
            };
            try {
                return new CallToolResponse {
                    Content = [
                        new Content {
                            Text = luaEngine.Call(code, []),
                        },
                        new Content {
                            Text = JsonSerializer.Serialize(new {
                                sessionId = sessionId_,
                            }),
                        },
                    ],
                };
            } catch (Exception ex) {
                Console.Error.WriteLine(ex.ToString());
                await server.SendNotificationAsync("error", new {
                    ex.Message,
                });
                return new CallToolResponse {
                    Content = [
                        new Content {
                            Text = JsonSerializer.Serialize(new JsonArray([null, ex.Message]))
                        },
                        new Content {
                            Text = JsonSerializer.Serialize(new {
                                sessionId = sessionId_,
                            }),
                        }
                    ],
                    IsError = true,
                };
            }
        }
        [McpServerTool(Destructive = false, ReadOnly = true), Description("get global variable list")]
        public static string[] ListGlobals(
            IServiceProvider services,
            [Description("session id to specify lua environment")] string sessionId,
            [Description("not filter libraries and misc")] bool noFilter = false
        )
        {
            var luaEngine = services.GetRequiredService<VMPool>().GetOrCreate(sessionId, out _);
            return luaEngine.GetListGlobals(!noFilter);
        }
    }
}
