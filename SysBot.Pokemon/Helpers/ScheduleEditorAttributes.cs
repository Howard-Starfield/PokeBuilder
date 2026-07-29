using System;

namespace SysBot.Pokemon.Helpers;

[AttributeUsage(AttributeTargets.Property)]
public sealed class RestartTimePickerAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class CurrentSystemTimeAttribute : Attribute;
