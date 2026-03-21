using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace FlightRecorder.Client.Generators
{
    [Generator]
    public class AiOperatorGenerator : BaseGenerator, ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
#if DEBUG
            if (!Debugger.IsAttached)
            {
                //Debugger.Launch();
            }
#endif
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var fields = GetSimConnectFields(context, AiAircraftPosition).ToList();
            AddSetStruct(context, fields);
            AddOperator(context, fields);
        }

        private static void AddSetStruct(GeneratorExecutionContext context, List<(string type, string name, string variable, string unit, int dataType, int? setType, string setByEvent, double min, double max, string defaultField)> fields)
        {
            var builder = new StringBuilder();
            builder.Append(@"
using System;
using FlightRecorder.Client;

namespace FlightRecorder.Client
{
    public partial struct AiAircraftPositionSetStruct
    {");

            foreach ((var type, var name, _, _, _, var setType, _, _, _, _) in fields)
            {
                if (setType == null || setType == SetTypeDefault)
                {
                    builder.Append($@"
        public {type} {name};");
                }
            }

            builder.Append(@"
    }
}");

            context.AddSource("SetStruct", SourceText.From(builder.ToString(), Encoding.UTF8));
        }

        private static void AddOperator(GeneratorExecutionContext context, List<(string type, string name, string variable, string unit, int dataType, int? setType, string setByEvent, double min, double max, string defaultField)> fields)
        {
            var builder = new StringBuilder();
            builder.Append(@"
using System;
using FlightRecorder.Client;

namespace FlightRecorder.Client
{
    public partial class AiAircraftPositionStructOperator
    {");

            builder.Append(@"
        public static partial AiAircraftPositionSetStruct ToSet(AiAircraftPositionStruct variables)
            => new AiAircraftPositionSetStruct
            {");
            foreach ((_, var name, _, _, _, var setType, _, _, _, _) in fields)
            {
                if (setType == null || setType == SetTypeDefault)
                {
                    builder.Append($@"
                {name} = variables.{name},");
                }
            }
            builder.Append(@"
            };
");

            builder.Append(@"
        public static AiAircraftPositionSetStruct Add(AiAircraftPositionSetStruct position1, AiAircraftPositionSetStruct position2)
            => new AiAircraftPositionSetStruct
            {");
            foreach ((_, var name, _, _, _, var setType, _, _, _, _) in fields)
            {
                if (setType == null || setType == SetTypeDefault)
                {
                    builder.Append($@"
                {name} = position1.{name} + position2.{name},");
                }
            }
            builder.Append(@"
            };
");

            builder.Append(@"
        public static AiAircraftPositionSetStruct Scale(AiAircraftPositionSetStruct position, double factor)
            => new AiAircraftPositionSetStruct
            {");
            foreach ((var type, var name, _, _, _, var setType, _, _, _, _) in fields)
            {
                if (setType == null || setType == SetTypeDefault)
                {
                    switch (type)
                    {
                        case "double":
                            builder.Append($@"
                {name} = position.{name} * factor,"); // TODO: support wrapping around for angle
                            break;
                        case "int":
                            builder.Append($@"
                {name} = (int)Math.Round(position.{name} * factor),");
                            break;
                        case "uint":
                            builder.Append($@"
                {name} = (uint)Math.Round(position.{name} * factor),");
                            break;
                        default:
                            // TODO: warning
                            break;
                    }
                }
            }
            builder.Append(@"
            };
");

            builder.Append(@"
        public static AiAircraftPositionSetStruct Interpolate(AiAircraftPositionSetStruct position1, AiAircraftPositionSetStruct position2, double interpolation)
            => new AiAircraftPositionSetStruct
            {");
            foreach ((var type, var name, _, _, _, var setType, _, var min, var max, _) in fields)
            {
                if (setType == null || setType == SetTypeDefault)
                {
                    switch (type)
                    {
                        case "double":
                            if (min < max)
                            {
                                builder.Append($@"
                {name} = InterpolateWrap(position1.{name}, position2.{name}, interpolation, {min}, {max}),");
                            }
                            else
                            {
                                builder.Append($@"
                {name} = position1.{name} * interpolation + position2.{name} * (1 - interpolation),");
                            }
                            break;
                        case "int":
                            builder.Append($@"
                {name} = (int)Math.Round(position1.{name} * interpolation + position2.{name} * (1 - interpolation)),");
                            break;
                        case "uint":
                            builder.Append($@"
                {name} = (uint)Math.Round(position1.{name} * interpolation + position2.{name} * (1 - interpolation)),");
                            break;
                        default:
                            // TODO: warning
                            break;
                    }
                }
            }
            builder.Append(@"
            };
");

            builder.Append(@"
    }
}");

            context.AddSource("AiOperatorGenerator", SourceText.From(builder.ToString(), Encoding.UTF8));
        }
    }
}
