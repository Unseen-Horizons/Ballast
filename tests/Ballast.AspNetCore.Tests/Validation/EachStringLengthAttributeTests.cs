// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.ComponentModel.DataAnnotations;

namespace Ballast.AspNetCore.Tests.Validation;

/// <summary>
/// <c>EachStringLengthAttribute</c>: every string of a list is checked against the cap, a null list or item passes, and a value that
/// is not a list of strings passes. The property is checked through <see cref="Validator"/>, as the framework checks it.
/// </summary>
public class EachStringLengthAttributeTests
{
    private sealed record Holder
    {
        [EachStringLength(3)]
        public IReadOnlyList<string?>? Items { get; init; }
    }

    private static bool Valid(object value) =>
        Validator.TryValidateObject(value, new ValidationContext(value), null, validateAllProperties: true);

    [Fact]
    public void A_null_list_passes() =>
        Assert.True(Valid(new Holder { Items = null }));

    [Fact]
    public void A_list_of_strings_at_the_cap_passes() =>
        Assert.True(Valid(new Holder { Items = ["abc", "de", ""] }));

    [Fact]
    public void A_string_one_character_past_the_cap_fails() =>
        Assert.False(Valid(new Holder { Items = ["abc", "defg"] }));

    [Fact]
    public void A_null_item_passes() =>
        Assert.True(Valid(new Holder { Items = ["abc", null] }));

    [Fact]
    public void A_value_that_is_not_a_list_of_strings_passes()
    {
        var attribute = new EachStringLengthAttribute(3);

        Assert.True(attribute.IsValid("a plain string well over the cap"));
        int[] numbers = [1, 22, 333];
        Assert.True(attribute.IsValid(numbers));
        Assert.True(attribute.IsValid(null));
    }

    [Fact]
    public void Max_is_the_cap_the_attribute_was_built_with() =>
        Assert.Equal(7, new EachStringLengthAttribute(7).Max);
}
