#nullable enable
using System;
using System.Linq;
using GreenDonut;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using NHibernate.Linq;
using WB.Core.BoundedContexts.Headquarters.Views.Interview;
using WB.Core.BoundedContexts.Headquarters.Views.Questionnaire;
using WB.Infrastructure.Native.Storage.Postgre;
using WB.UI.Headquarters.Controllers.Api.PublicApi.Graphql.Questionnaires;

namespace WB.UI.Headquarters.Controllers.Api.PublicApi.Graphql.Interviews
{
    public class AnswerObjectType : ObjectType<IdentifyEntityValue>
    {
        protected override void Configure(IObjectTypeDescriptor<IdentifyEntityValue> descriptor)
        {
            descriptor.BindFieldsExplicitly();
            
            descriptor.Name("IdentifyingEntity").Description("Identifying variable or question");
            
            descriptor.Field(x => x.AnswerCode)
                .Type<IntType>()
                .Name("answerValue")
                .Description("Answer value for categorical questions");

            descriptor.Field(x => x.Entity)
                .Name("entity")
                .Resolve(async context =>
                {
                    var loader = context.DataLoader<QuestionnaireCompositeItemDataLoader>();
                    return await loader.LoadAsync(context.Parent<IdentifyEntityValue>().Entity.Id, context.RequestAborted);
                })
                .Type<NonNullType<EntityItemObjectType>>();
            
            descriptor.Field(x => x.Value)
                .Name("value")
                .Type<StringType>();


            descriptor.Field(x => x.ValueBool)
                .Type<BooleanType>()
                .Name("valueBool")
                .Description("Bool answer value");

            descriptor.Field(x => x.ValueDate)
                .Type<DateTimeType>()
                .Name("valueDate")
                .Description("Date answer value");

            descriptor.Field(x => x.ValueLong)
                .Type<LongType>()
                .Name("valueLong")
                .Description("Long answer value");

            descriptor.Field(x => x.ValueDouble)
                .Type<FloatType>()
                .Name("valueDouble")
                .Description("Double answer value");

            descriptor.Field(x => x.IsEnabled)
                .Name("isEnabled")
                .Type<BooleanType>();
        }
    }
}
