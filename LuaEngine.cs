using LuaNET.Lua54;
using static LuaNET.Lua54.Lua;
using System.Runtime.InteropServices;
using static LuaMCP.Utils;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace LuaMCP {
    static class LuaStateExtension {
        public static int PushResult(this lua_State L, string? err) {
            if (err != null) {
                lua_pushnil(L);
                lua_pushstring(L, err);
                return 2;
            }
            lua_pushboolean(L, 1);
            return 1;
        }
    }

    public class LuaEngine: IDisposable {
#region limitations
        private static bool _allowRequireDll = false;
        private static bool _allowLoadlib = false;
        private static bool _unjailIO = false;
        private static bool _allowJailIO = false;
        private static bool _processExecute = false;
        private static bool _openDebugLib = false;

#endregion
        public readonly lua_State L;
        public static Action<object?[]> onPrint = _ => { };

        public LuaEngine() {
            L = luaL_newstate();
            OpenLibs();
        }

        public void Dispose() {
            lua_close(L);
        }

        private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions {
            WriteIndented = true,
        };

        public string Call(string chunk, object?[] values) {
            var arr = new JsonArray();
            if (luaL_loadstring(L, chunk) == LUA_OK) {
                L.PushValues(values);
                var result = lua_pcall(L, values.Length, LUA_MULTRET, 0);
                if (result != LUA_OK) {
                    var err = lua_tostring(L, -1);
                    lua_pop(L, 1);
                    throw new Exception(err);
                }
            } else {
                lua_pushnil(L);
                lua_insert(L, 1);
            }
            var nresults = lua_gettop(L);
            var results = L.PopValues(nresults);
            lua_pop(L, lua_gettop(L));
            foreach (var item in results) {
                if (item is JsonNode node) arr.Add(node);
                else arr.Add(JsonValue.Create(item));
            }
            return JsonSerializer.Serialize(arr, _jsonSerializerOptions);
        }

        public string[] GetListGlobals(bool filter) {
            var stackTop = lua_gettop(L);
            var filter_ = filter
                ? "local filter = { getmetatable = true, utf8 = true, math = true, pairs = true, tonumber = true, error = true, rawlen = true, table = true, rawget = true, select = true, print = true, setmetatable = true, assert = true, require = true, pcall = true, ipairs = true, xpcall = true, os = true, _VERSION = true, rawset = true, _G = true, package = true, rawequal = true, type = true, io = true, tostring = true, load = true, next = true, coroutine = true, string = true, warn = true, collectgarbage = true };"
                : " local filter = {};";
            luaL_dostring(L, $@"
                do
                    {filter_}
                    local t = {{}}
                    local i = 1
                    for k, v in pairs(_G) do
                        if not filter[k] then
                            t[i] = k
                            i = i + 1
                        end
                    end
                    return table.unpack(t)
                end
            ");
            var array = L.PopValues(lua_gettop(L) - stackTop);
            var result = new string[array.Length];
            for (int i = 0; i < array.Length; i++) {
                if (array[i] is string str) result[i] = str;
                else result[i] = array[i]?.ToString() ?? "nil";
            }
            return result;
        }

        private void OpenLibs() {
            // base 基本ライブラリ
            luaL_requiref(L, LUA_GNAME, luaopen_base, 1); lua_pop(L, 1);

            int type_ = lua_rawgeti(L, LUA_REGISTRYINDEX, LUA_RIDX_GLOBALS);
            lua_pushnil(L);
            // lua_pushcfunction(L, static (L) => _dofile(L));
            lua_setfield(L, -2, "dofile");
            lua_pushnil(L);
            // lua_pushcfunction(L, static (L) => _loadfile(L));
            lua_setfield(L, -2, "loadfile");
            lua_pop(L, 1); // pop global

            lua_pushcfunction(L, static (L) => {
                onPrint(L.PopValues(lua_gettop(L)));
                return 0;
            }); // set print function
            lua_setglobal(L, "print"); // set _G['print'] = print

            // readonly package library
            luaL_getsubtable(L, LUA_REGISTRYINDEX, LUA_LOADED_TABLE);
            lua_pushstring(L, LOADLIBNAME);
            lua_pushlightuserdata(L, (nuint)ReadonlyPackageLibraryUserDataPointer); // package library object
            lua_newtable(L); // metatable
            lua_pushstring(L, "__metatable");
            lua_pushstring(L, "readonly");
            lua_rawset(L, -3); // set __metatable = "readonly"
            lua_pushstring(L, "__newindex");
            lua_pushcfunction(L, static (L) => {
                Console.Error.WriteLine("package library is readonly");
                return 0;
            });
            lua_rawset(L, -3); // set __newindex = function() return nil, errmsg end
            lua_pushstring(L, "__index");
            // #### package library start
            // luaopen_package パッケージライブラリ
            luaL_requiref(L, LOADLIBNAME, luaopen_package, 1);

            lua_pushstring(L, "path");
            lua_pushstring(L, "./?.lua;./?.lc;./?/init.lua;./lib/?.lua;./lib/?.lc;./lib/?/init.lua");
            lua_rawset(L, -3); // set package.path
            lua_pushstring(L, "cpath");
            lua_pushstring(L, _allowRequireDll ? "../?.dll" : "");
            lua_rawset(L, -3); // set package.cpath
            if (!_allowLoadlib) {
                lua_pushstring(L, "loadlib");
                lua_pushcfunction(L, static (L) => {
                    return L.PushResult("package.loadlib() is not allowed");
                });
                lua_rawset(L, -3); // set package.loadlib
            }
            if (!_unjailIO) {
                // package.searchpath() は任意のファイルが存在するか（読み込みモードで開くことができるか）を知るために使えるので塞ぐ
                lua_pushstring(L, "searchpath");
                lua_pushcfunction(L, static (L) => {
                    return L.PushResult("package.searchpath() is not allowed");
                });
                lua_rawset(L, -3); // set package.searchpath
            }
            // #### package library end
            lua_pushcclosure(L, static (L) => {
                if (lua_gettop(L) != 2) { lua_pushnil(L); return 1; }
                var key = lua_tostring(L, 2);
                if (key == null) { lua_pushnil(L); return 1; }
                lua_rawget(L, lua_upvalueindex(1));
                return 1;
            }, 1);
            lua_rawset(L, -3); // __index = function(_, k) return package[k] end
            lua_setmetatable(L, -2); // set metatable
            lua_rawset(L, -3); // package.loaded['package'] = readonly_package
            lua_pushstring(L, LOADLIBNAME);
            lua_rawget(L, -2);
            lua_pushvalue(L, -1); // package.loaded['package'] for next
            lua_setglobal(L, LOADLIBNAME); // set _G['package'] = package.loaded['package']
            // set upvalue
            lua_getglobal(L, "require");
            lua_insert(L, -2);
            lua_setupvalue(L, -2, 1); // set upvalue 1 of require **as** package
            lua_setglobal(L, "require"); // set _G['require'] = require
            lua_getglobal(L, LOADLIBNAME);
            lua_getfield(L, -1, "searchers"); // package.searchers
            lua_remove(L, -2);
            lua_geti(L, -1, 1); // package.searchers[1]
            lua_getglobal(L, LOADLIBNAME);
            lua_setupvalue(L, -2, 1); // set upvalue 1 of package.searchers[1] **as** package
            lua_pop(L, 1); // pop package.searchers[1]
            lua_geti(L, -1, 2); // package.searchers[2]
            lua_getglobal(L, LOADLIBNAME);
            lua_setupvalue(L, -2, 1);
            lua_pop(L, 1);
            lua_geti(L, -1, 3); // package.searchers[3]
            lua_getglobal(L, LOADLIBNAME);
            lua_setupvalue(L, -2, 1);
            lua_pop(L, 1);
            lua_geti(L, -1, 4); // package.searchers[4]
            lua_getglobal(L, LOADLIBNAME);
            lua_setupvalue(L, -2, 1);
            lua_pop(L, 1); // pop package.searchers[4]

            lua_pop(L, 2); // pop package.searchers package.loaded

            // coroutine コルーチンライブラリ
            luaL_requiref(L, LUA_COLIBNAME, luaopen_coroutine, 1); lua_pop(L, 1);
            // table: テーブルライブラリ
            luaL_requiref(L, LUA_TABLIBNAME, luaopen_table, 1); lua_pop(L, 1);

            // io: 入出力ライブラリ
            luaL_requiref(L, IOLIBNAME, luaopen_io, 1);
            if (!_unjailIO) {
                lua_getfield(L, -1, "input");
                lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
                lua_setfield(L, -2, "input");
                lua_getfield(L, -1, "lines");
                lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
                lua_setfield(L, -2, "lines");
                lua_getfield(L, -1, "open");
                lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
                lua_setfield(L, -2, "open");
                lua_getfield(L, -1, "output");
                lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
                lua_setfield(L, -2, "output");
            }

            if (!_processExecute) {
                // io.popen() を塞ぐ
                lua_pushcfunction(L, static (L) => {
                    return L.PushResult("io.popen() is not allowed");
                });
                lua_setfield(L, -2, "popen");
            }
            lua_pop(L, 1); // pop io library

            // os: OSライブラリ
            luaL_requiref(L, OSLIBNAME, luaopen_os, 1);
            if (!_processExecute) {
                // os.execute() を塞ぐ
                lua_pushcfunction(L, static (L) => {
                    return L.PushResult("os.execute() is not allowed");
                });
                lua_setfield(L, -2, "execute");
            }
            // os.exit() 塞ぐ
            lua_pushcfunction(L, static (L) => {
                lua_pushstring(L, "os.exit() is called");
                return lua_error(L);
            });
            lua_setfield(L, -2, "exit");

            if (!_unjailIO) {
                lua_getfield(L, -1, "remove");
                lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall(L), 1);
                lua_setfield(L, -2, "remove");
                lua_getfield(L, -1, "rename");
                lua_pushcclosure(L, static (L) => _wrappedIOFunctionCall2(L), 1);
                lua_setfield(L, -2, "rename");
            }
            lua_pop(L, 1);

            // string: 文字列ライブラリ
            luaL_requiref(L, LUA_STRLIBNAME, luaopen_string, 1); lua_pop(L, 1);
            // math: 数学ライブラリ
            luaL_requiref(L, LUA_MATHLIBNAME, luaopen_math, 1); lua_pop(L, 1);
            // utf8: UTF8ライブラリ
            luaL_requiref(L, LUA_UTF8LIBNAME, luaopen_utf8, 1); lua_pop(L, 1);

            // debug: デバッグライブラリ
            if (_openDebugLib) {
                luaL_requiref(L, DBLIBNAME, luaopen_debug, 1); lua_pop(L, 1);
            }

            // 削除した関数をメモリ上から追い出す（気休め）
            lua_gc(L, LUA_GCCOLLECT, 0);
        }

        private static int _wrappedIOFunctionCall(lua_State L) {
            if (!_allowJailIO) return L.PushResult("io operation is not allowed.");
            var nargs = lua_gettop(L);
            if (nargs >= 1 && lua_isstring(L, 1) == 1) {
                var file = luaL_checkstring(L, 1);
                if (file != null && !IsInIODirectory(file, out var error)) return L.PushResult(error);
            }
            lua_pushvalue(L, lua_upvalueindex(1));
            lua_insert(L, 1); // 上位値から取り出した io.input 等をスタックの底に送り込む
            lua_call(L, nargs, LUA_MULTRET);
            return lua_gettop(L);
        }
        private static int _wrappedIOFunctionCall2(lua_State L) {
            if (!_allowJailIO) return L.PushResult("io operation is not allowed.");
            var nargs = lua_gettop(L);
            if (nargs >= 1 && lua_isstring(L, 1) == 1) {
                var file = luaL_checkstring(L, 1);
                if (file != null && !IsInIODirectory(file, out var error)) return L.PushResult(error);
            }
            if (nargs >= 2 && lua_isstring(L, 2) == 1) {
                var file = luaL_checkstring(L, 2);
                if (file != null && !IsInIODirectory(file, out var error)) return L.PushResult(error);
            }
            lua_pushvalue(L, lua_upvalueindex(1));
            lua_insert(L, 1); // 上位値から取り出した io.input 等をスタックの底に送り込む
            lua_call(L, nargs, LUA_MULTRET);
            return lua_gettop(L);
        }
        public static string IODirectory => Path.Combine(Environment.CurrentDirectory, "io_dir");

        private static bool IsInIODirectory(string path, out string? error) {
            var result = IsInDirectory(IODirectory, path, out error);
            error ??= $"io operation is not allowed. not in io directory";
            return result;
        }

        private static readonly IntPtr ReadonlyPackageLibraryUserDataPointer = Marshal.AllocHGlobal(sizeof(int));
    }
}
