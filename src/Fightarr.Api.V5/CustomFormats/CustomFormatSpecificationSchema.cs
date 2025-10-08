using NzbDrone.Core.CustomFormats;
using Fightarr.Http.ClientSchema;
using Fightarr.Http.REST;

namespace Fightarr.Api.V5.CustomFormats
{
    public class CustomFormatSpecificationSchema : RestResource
    {
        public required string Name { get; set; }
        public required string Implementation { get; set; }
        public required string ImplementationName { get; set; }
        public required string InfoLink { get; set; }
        public bool Negate { get; set; }
        public bool Required { get; set; }
        public required List<Field> Fields { get; set; }
        public List<CustomFormatSpecificationSchema>? Presets { get; set; }
    }

    public static class CustomFormatSpecificationSchemaMapper
    {
        public static CustomFormatSpecificationSchema ToSchema(this ICustomFormatSpecification model)
        {
            return new CustomFormatSpecificationSchema
            {
                Name = model.Name,
                Implementation = model.GetType().Name,
                ImplementationName = model.ImplementationName,
                InfoLink = model.InfoLink,
                Negate = model.Negate,
                Required = model.Required,
                Fields = SchemaBuilder.ToSchema(model)
            };
        }
    }
}
