using System;
using Newtonsoft.Json;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace GenerateBindingRedirects
{
    public class ToStringJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            objectType == typeof(VersionRange) ||
            objectType == typeof(NuGetDependency) ||
            objectType == typeof(RuntimeAssembly) ||
            objectType == typeof(NuGetVersion);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue(value.ToString());

        public override bool CanRead => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotImplementedException();
    }
}