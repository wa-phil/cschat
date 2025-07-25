using Xunit;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace unittests
{
    public class JSONParserWriterTests
    {
        public class TestClass
        {
            public int IntValue { get; set; }
            public string StringValue { get; set; }
            public List<double> ListValue { get; set; }
        }

        [DataContract]
        public class DataMemberTestClass
        {
            [DataMember(Name = "renamed")]
            public int OriginalName { get; set; }
            [IgnoreDataMember]
            public string Ignored { get; set; }
            public string Normal { get; set; }
        }

        [Fact]
        public void JSONParser_ShouldParsePrimitives()
        {
            Assert.Equal(123, "123".FromJson<int>());
            Assert.Equal(123.45, "123.45".FromJson<double>());
            Assert.Equal("hello", "\"hello\"".FromJson<string>());
            Assert.True("true".FromJson<bool>());
            Assert.False("false".FromJson<bool>());
        }

        [Fact]
        public void JSONParser_ShouldParseList()
        {
            var list = "[1,2,3]".FromJson<List<int>>();
            Assert.Equal(new List<int> { 1, 2, 3 }, list);
        }

        [Fact]
        public void JSONParser_ShouldParseDictionary()
        {
            var dict = "{\"a\":1,\"b\":2}".FromJson<Dictionary<string, int>>();
            Assert.Equal(1, dict["a"]);
            Assert.Equal(2, dict["b"]);
        }

        [Fact]
        public void JSONParser_ShouldParseObject()
        {
            var json = "{\"IntValue\":42,\"StringValue\":\"foo\",\"ListValue\":[1.1,2.2]}";
            var obj = json.FromJson<TestClass>()!;
            Assert.Equal(42, obj.IntValue);
            Assert.Equal("foo", obj.StringValue);
            Assert.Equal(new List<double> { 1.1, 2.2 }, obj.ListValue);
        }

        [Fact]
        public void JSONWriter_ShouldWritePrimitives()
        {
            Assert.Equal("123", 123.ToJson());
            Assert.Equal("123.45", 123.45.ToJson());
            Assert.Equal("\"hello\"", "hello".ToJson());
            Assert.Equal("true", true.ToJson());
            Assert.Equal("false", false.ToJson());
        }

        [Fact]
        public void JSONWriter_ShouldWriteList()
        {
            var list = new List<int> { 1, 2, 3 };
            Assert.Equal("[1,2,3]", list.ToJson());
        }

        [Fact]
        public void JSONWriter_ShouldWriteDictionary()
        {
            var dict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
            var json = dict.ToJson();
            Assert.Contains("\"a\":1", json);
            Assert.Contains("\"b\":2", json);
            Assert.StartsWith("{", json);
            Assert.EndsWith("}", json);
        }

        [Fact]
        public void JSONWriter_ShouldWriteObject()
        {
            var obj = new TestClass { IntValue = 42, StringValue = "foo", ListValue = new List<double> { 1.1, 2.2 } };
            var json = obj.ToJson();
            Assert.Contains("\"IntValue\":42", json);
            Assert.Contains("\"StringValue\":\"foo\"", json);
            Assert.Contains("\"ListValue\":[1.1,2.2]", json);
            Assert.StartsWith("{", json);
            Assert.EndsWith("}", json);
        }

        [Theory]
        [InlineData("\\\"Hello\\\"", "\"Hello\"")]
        [InlineData("Line\\nBreak", "Line\nBreak")]
        [InlineData("Tab\\tChar", "Tab\tChar")]
        [InlineData("Carriage\\rReturn", "Carriage\rReturn")]
        [InlineData("Backspace\\bTest", "Backspace\bTest")]
        [InlineData("FormFeed\\fTest", "FormFeed\fTest")]
        [InlineData("Unicode\\u263A", "Unicode☺")]
        [InlineData("Backslash\\\\Test", "Backslash\\Test")]
        public void JSONParser_ShouldParseEscapedCharacters(string jsonString, string expected)
        {
            var json = $"\"{jsonString}\"";
            var parsed = json.FromJson<string>();
            Assert.Equal(expected, parsed);
        }

        [Theory]
        [InlineData("\"Hello\"", "\\\"Hello\\\"")]
        [InlineData("Line\nBreak", "Line\\nBreak")]
        [InlineData("Tab\tChar", "Tab\\tChar")]
        [InlineData("Carriage\rReturn", "Carriage\\rReturn")]
        [InlineData("Backspace\bTest", "Backspace\\bTest")]
        [InlineData("FormFeed\fTest", "FormFeed\\fTest")]
        [InlineData("Unicode☺", "Unicode\\u263A")]
        [InlineData("Backslash\\Test", "Backslash\\\\Test")]
        public void JSONWriter_ShouldWriteEscapedCharacters(string input, string expectedEscaped)
        {
            var json = input.ToJson();
            // Remove surrounding quotes for comparison
            var inner = json.Substring(1, json.Length - 2);
            Assert.Equal(expectedEscaped, inner);
        }

        [Fact]
        public void JSONWriter_ShouldHonorDataMemberAttributes()
        {
            var obj = new DataMemberTestClass
            {
                OriginalName = 5,
                Ignored = "should not appear",
                Normal = "normal"
            };
            var json = obj.ToJson();
            Assert.Contains("\"renamed\":5", json); // DataMember(Name)
            Assert.DoesNotContain("Ignored", json);    // IgnoreDataMember
            Assert.Contains("\"Normal\":\"normal\"", json); // Normal property
        }

        [Fact]
        public void JSONParser_ShouldHonorDataMemberAttributes()
        {
            var json = "{\"renamed\":42,\"Ignored\":\"no\",\"Normal\":\"ok\"}";
            var obj = json.FromJson<DataMemberTestClass>();
            Assert.Equal(42, obj.OriginalName); // DataMember(Name)
            Assert.Null(obj.Ignored);           // IgnoreDataMember
            Assert.Equal("ok", obj.Normal);    // Normal property
        }

        [Fact]
        public void JSONParser_ShouldParseThis()
        {
            const string json = @"
            {
                ""choices"": [
                    {
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""Hello, how can I help you?""
                        }
                    }
                ]
            }";

            var obj = json.FromJson<Dictionary<string, object>>();
            Assert.NotNull(obj);
            Assert.True(obj.TryGetValue("choices", out var choicesRaw));
            var choices = Assert.IsType<List<object>>(choicesRaw);
            var choice = Assert.IsType<Dictionary<string, object>>(choices[0]);
            var message = Assert.IsType<Dictionary<string, object>>(choice["message"]);
            var content = Assert.IsType<string>(message["content"]);
            Assert.Equal("Hello, how can I help you?", content);
        }

        [Fact]
        public void JSONParser_ShouldHandleNulls()
        {
            var json = "{\"value\":null}";
            var obj = json.FromJson<Dictionary<string, object>>();
            Assert.NotNull(obj);
            Assert.True(obj.TryGetValue("value", out var value));
            Assert.Null(value); // Should be null
        }

        [Fact]
        public void JSONParser_ShouldHandleEmptyObjects()
        {
            var json = "{}";
            var obj = json.FromJson<Dictionary<string, object>>();
            Assert.NotNull(obj);
            Assert.Empty(obj); // Should be empty  
        }

        [Fact]
        public void JSONWriter_ShouldHandleNulls()
        {
            var obj = new Dictionary<string, object> { { "value", null } };
            var json = obj.ToJson();
            Assert.Equal("{\"value\":null}", json);
        }

        [Fact]
        public void JSONWriter_ShouldHandleEmptyObjects()
        {
            var obj = new Dictionary<string, object>();
            var json = obj.ToJson();
            Assert.Equal("{}", json); // Should be empty object
        }

        [Fact]
        public void JSONParser_ShouldHandleNestedObjects()
        {
            var json = @"
            {
                ""outer"": {
                    ""inner"": {
                        ""key"": ""value""
                    }
                }
            }";

            var obj = json.FromJson<Dictionary<string, object>>();
            Assert.NotNull(obj);
            Assert.True(obj.TryGetValue("outer", out var outerRaw));
            var outer = Assert.IsType<Dictionary<string, object>>(outerRaw);
            Assert.True(outer.TryGetValue("inner", out var innerRaw));
            var inner = Assert.IsType<Dictionary<string, object>>(innerRaw);
            Assert.True(inner.TryGetValue("key", out var keyRaw));
            var key = Assert.IsType<string>(keyRaw);
            Assert.Equal("value", key);
        }

        [Fact]
        public void JSONWriter_ShouldHandleNestedObjects()
        {
            var obj = new Dictionary<string, object>
            {
                { "outer", new Dictionary<string, object>
                    {
                        { "inner", new Dictionary<string, object>
                            {
                                { "key", "value" }
                            }
                        }
                    }
                }
            };

            var json = obj.ToJson();
            Assert.Equal("{\"outer\":{\"inner\":{\"key\":\"value\"}}}", json);
        }

        [Fact]
        public void JSONParser_ShouldHandleComplexTypes()
        {
            var json = @"
            {
                ""complex"": {
                    ""intValue"": 123,
                    ""stringValue"": ""test"",
                    ""listValue"": [1.1, 2.2, 3.3]
                }
            }";

            var obj = json.FromJson<Dictionary<string, object>>();
            Assert.NotNull(obj);
            Assert.True(obj.TryGetValue("complex", out var complexRaw));
            var complex = Assert.IsType<Dictionary<string, object>>(complexRaw);

            Assert.True(complex.TryGetValue("intValue", out var intValueRaw));
            Assert.True(intValueRaw is int or double);
            Assert.Equal(123, Convert.ToInt32(intValueRaw));

            Assert.True(complex.TryGetValue("stringValue", out var stringValueRaw));
            Assert.Equal("test", stringValueRaw as string);

            Assert.True(complex.TryGetValue("listValue", out var listValueRaw));
            var listValue = Assert.IsType<List<object>>(listValueRaw);
            Assert.Equal(3, listValue.Count);
            Assert.Equal(1.1, Assert.IsType<double>(listValue[0]));
            Assert.Equal(2.2, Assert.IsType<double>(listValue[1]));
            Assert.Equal(3.3, Assert.IsType<double>(listValue[2]));
        }

        [Fact]
        public void JSONLibrary_ShouldHandleThis()
        {
            var json = @"
            {
                ""Timestamp"": ""07/03/2025 04:29:08"",
                ""Level"": ""Information"",
                ""Method"": ""Engine.SetProvider"",
                ""Source"": ""IChatProvider.cs:232"",
                ""Success"": true,
                ""Provider"": ""AzureAI"",
                ""ProviderSet"": true
            }";
            var result = json.FromJson<Dictionary<string, object>>();
            Assert.NotNull(result);
            Assert.Equal("07/03/2025 04:29:08", result["Timestamp"]);
            Assert.Equal("Information", result["Level"]);
            Assert.Equal("Engine.SetProvider", result["Method"]); 
            Assert.Equal("IChatProvider.cs:232", result["Source"]);
            Assert.True((bool)result["Success"]);
            Assert.Equal("AzureAI", result["Provider"]);
            Assert.True((bool)result["ProviderSet"]);

            var obj = new Dictionary<string, object>
            {
                { "Timestamp", "07/03/2025 04:29:08" },
                { "Level", "Information" },
                { "Method", "Engine.SetProvider" },
                { "Source", "IChatProvider.cs:232" },
                { "Success", true },
                { "Provider", "AzureAI" },
                { "ProviderSet", true }
            };

            var output = obj.ToJson();
            Assert.Equal("{\"Timestamp\":\"07/03/2025 04:29:08\",\"Level\":\"Information\",\"Method\":\"Engine.SetProvider\",\"Source\":\"IChatProvider.cs:232\",\"Success\":true,\"Provider\":\"AzureAI\",\"ProviderSet\":true}", output);
        }
    }
}
