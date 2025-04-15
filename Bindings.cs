using LuaNET.Lua54;
using static LuaNET.Lua54.Lua;
using System.Text.Json.Nodes;

namespace LuaMCP {
    public static class Bindings {

        public static void PrintStack(lua_State L) {
            int top = lua_gettop(L);
            Console.Error.WriteLine($"-------- {top}");
            for (int i = top; i > 0; i--) {
                var str = lua_type(L, i) == LUA_TNUMBER ? lua_tonumber(L, i).ToString() : lua_tostring(L, i);
                var t = lua_typename(L, lua_type(L, i));
                Console.Error.WriteLine($"{i} {i - top - 1} {t} {str}");
            }
            Console.Error.WriteLine($"----------");
        }
        private static bool IsArray(lua_State L, int idx) {
            idx = lua_absindex(L, idx);
            long len = luaL_len(L, idx);
            long n = 0;
            lua_pushnil(L);
            while (lua_next(L, idx) != 0) {
                n++;
                if (lua_type(L, -2) != LUA_TNUMBER) {
                    lua_pop(L, 2);
                    return false;
                }
                lua_pop(L, 1);
            }
            if (n > 0) {
                var t1 = lua_rawgeti(L, idx, 1);
                lua_pop(L, 1);
                if (t1 == LUA_TNIL) return false;
            }
            return n == len;
        }
        // スタックトップにある値を JsonNode で返す。スタックの値は削除されない
        public static JsonNode? LuaObjectToJsonNode(lua_State L, int idx = -1) {
            // PrintStack(L);
            switch (lua_type(L, idx)) {
                case LUA_TNIL: return null;
                case LUA_TNUMBER: return JsonValue.Create(lua_tonumber(L, idx));
                case LUA_TBOOLEAN: return JsonValue.Create(lua_toboolean(L, idx) != 0);
                case LUA_TSTRING: return JsonValue.Create(lua_tostring(L, idx));
                case LUA_TTABLE:
                    if (lua_checkstack(L, 2) == 0) throw new Exception("Lua stack limit");
                    if (IsArray(L, idx)) {
                        var arr = new JsonArray();
                        var len = luaL_len(L, idx);
                        for (int i = 1; i <= len; i++) {
                            lua_rawgeti(L, idx, i);
                            arr.Add(LuaObjectToJsonNode(L));
                            lua_pop(L, 1);
                        }
                        return arr;
                    } else {
                        var table = new JsonObject();
                        var idx_ = lua_absindex(L, idx);
                        lua_pushnil(L);
                        while (lua_next(L, idx_) != 0) {
                            string? key = lua_type(L, idx_) switch
                            {
                                LUA_TSTRING => lua_tostring(L, idx_),
                                LUA_TNUMBER => lua_tonumber(L, idx_).ToString(),
                                LUA_TBOOLEAN => lua_toboolean(L, idx_) != 0 ? "true" : "false",
                                _ => null,
                            };
                            var value = LuaObjectToJsonNode(L, -1);
                            if (key != null) table.Add(key, value);
                            lua_pop(L, 1);
                        }
                        return table;
                    }
                case LUA_TFUNCTION: // TODO: 実行する？
                    return null;
                case LUA_TUSERDATA: // fallthrough // TODO: メタテーブルみる
                case LUA_TLIGHTUSERDATA: // TODO: メタテーブルみる
                    return null;
                case LUA_TTHREAD: // TODO: 実行する？
                    return null;
                default:
                    return null;
            }
        }

        public static object?[] PopValues(this lua_State L, int n) {
            var arr = new object?[n];
            var start = lua_gettop(L) - n + 1;
            for (int i = 0; i < n; i++) {
                var idx = start + i;
                switch (lua_type(L, idx)) {
                    case LUA_TNIL:
                        arr[i] = null;
                        break;
                    case LUA_TNUMBER:
                        arr[i] = lua_tonumber(L, idx);
                        break;
                    case LUA_TBOOLEAN:
                        arr[i] = lua_toboolean(L, idx) != 0;
                        break;
                    case LUA_TSTRING:
                        arr[i] = lua_tostring(L, idx);
                        break;
                    case LUA_TTABLE:
                        arr[i] = LuaObjectToJsonNode(L, idx);
                        break;
                    default:
                        Console.Error.WriteLine($"Not implemented: {lua_typename(L, lua_type(L, idx))}");
                        arr[i] = null;
                        break;
                }
            }
            lua_pop(L, n);
            return arr;
        }
        public static int PushValues(this lua_State L, object?[] values) {
            foreach (var value in values) {
                switch (value) {
                    case bool b:
                        lua_pushboolean(L, b ? 1 : 0);
                        break;
                    case double d:
                        lua_pushnumber(L, d);
                        break;
                    case float f:
                        lua_pushnumber(L, f);
                        break;
                    case int i:
                        lua_pushinteger(L, i);
                        break;
                    case long l:
                        lua_pushinteger(L, l);
                        break;
                    case byte b:
                        lua_pushinteger(L, b);
                        break;
                    case string s:
                        lua_pushstring(L, s);
                        break;
                    case null:
                        lua_pushnil(L);
                        break;
                    default:
                        Console.Error.WriteLine($"Not implemented: {value?.GetType().Name ?? "null"}");
                        lua_pushnil(L);
                        break;
                }
            }
            return values.Length;
        }
    }
}
