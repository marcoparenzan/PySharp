// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.M16_FastApi;

/// <summary>
/// A broad, real-world sweep of pydantic v1 field types/validators/Config options — the author's
/// own "the goal is to share this project, so it needs to be robust" push past what
/// `samples/fastapi_demo.py` alone happened to exercise (FASTAPI_PLAN.md Phase 4.5). Two rounds of
/// ~30 real-world patterns were probed by hand first; most already worked (nested models,
/// List[Model], Enum fields, Field() constraints, Union fields, Field(default_factory=...),
/// parse_obj/.json()/parse_raw, Config.orm_mode/from_orm) — this file covers the ones that found a
/// real, previously-latent gap, each with its own regression test at the interpreter level too
/// (ClassTests/DateTimeTests/CollectionsAbcSetTests in M6_Stdlib/M4_Functions) plus the specific
/// real pydantic-level scenario here, so a future regression is caught at both layers.
/// </summary>
public class PydanticFieldTypeSweepTests : IClassFixture<PydanticInstallFixture>
{
    private readonly PydanticInstallFixture _fixture;

    public PydanticFieldTypeSweepTests(PydanticInstallFixture fixture) => _fixture = fixture;

    private string Run(string body)
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);
        engine.Run(body);
        return writer.ToString().TrimEnd('\n');
    }

    [Fact]
    public void Validator_decorator_transforms_a_field_value()
        // Real gap: `@validator` internally does `f_cls = classmethod(f)` then reads
        // `f_cls.__func__` and `setattr(f_cls, '__validator_config__', ...)` — both raised
        // AttributeError on a raw classmethod object before this round.
        => Assert.Equal("ABC", Run("""
            from pydantic import BaseModel, validator

            class M(BaseModel):
                name: str

                @validator("name")
                def upper(cls, v):
                    return v.upper()

            print(M(name="abc").name)
            """));

    [Fact]
    public void Root_validator_sees_all_fields_together()
        => Assert.Equal("3", Run("""
            from pydantic import BaseModel, root_validator

            class M(BaseModel):
                a: int
                b: int

                @root_validator
                def check_sum(cls, values):
                    if values.get("a", 0) + values.get("b", 0) > 100:
                        raise ValueError("sum too big")
                    return values

            print(M(a=1, b=2).a + 2)
            """));

    [Fact]
    public void Pre_validator_runs_before_type_coercion()
        => Assert.Equal("hello", Run("""
            from pydantic import BaseModel, validator

            class M(BaseModel):
                tags: str

                @validator("tags", pre=True)
                def strip(cls, v):
                    return v.strip() if isinstance(v, str) else v

            print(M(tags="  hello  ").tags)
            """));

    [Fact]
    public void Validator_can_apply_to_multiple_fields_at_once()
        => Assert.Equal("x y", Run("""
            from pydantic import BaseModel, validator

            class M(BaseModel):
                a: str
                b: str

                @validator("a", "b")
                def not_empty(cls, v):
                    if not v:
                        raise ValueError("empty")
                    return v

            m = M(a="x", b="y")
            print(m.a, m.b)
            """));

    [Fact]
    public void Real_iso_datetime_string_parses_via_the_datetime_field_validator()
        // Real gap: pydantic's own datetime_parse.py builds `datetime(**kw_)` — entirely by
        // keyword — after regex-parsing the string; the constructor only read positional args.
        => Assert.Equal("2024", Run("""
            from pydantic import BaseModel
            from datetime import datetime

            class M(BaseModel):
                created: datetime

            print(M(created="2024-01-15T12:30:45").created.year)
            """));

    [Fact]
    public void Conint_and_constr_enforce_real_constraints()
        => Assert.Equal("30\nAB", Run("""
            from pydantic import BaseModel, conint, constr, ValidationError

            class M(BaseModel):
                age: conint(gt=0, lt=150)
                code: constr(min_length=2, max_length=4, to_upper=True)

            m = M(age=30, code="ab")
            print(m.age)
            print(m.code)
            """));

    [Fact]
    public void Config_extra_forbid_rejects_an_unknown_field()
        => Assert.Equal("caught", Run("""
            from pydantic import BaseModel, ValidationError

            class M(BaseModel):
                class Config:
                    extra = "forbid"
                a: int

            try:
                M(a=1, b=2)
            except ValidationError:
                print("caught")
            """));

    [Fact]
    public void Field_alias_round_trips_through_dict_by_alias()
        => Assert.Equal("5\n{'itemId': 5}", Run("""
            from pydantic import BaseModel, Field

            class M(BaseModel):
                item_id: int = Field(..., alias="itemId")

            m = M(itemId=5)
            print(m.item_id)
            print(m.dict(by_alias=True))
            """));

    [Fact]
    public void Model_inheritance_combines_base_and_child_fields()
        => Assert.Equal("1 x", Run("""
            from pydantic import BaseModel

            class Base(BaseModel):
                id: int

            class Child(Base):
                name: str

            c = Child(id=1, name="x")
            print(c.id, c.name)
            """));

    [Fact]
    public void Copy_with_update_overrides_fields_without_mutating_the_original()
        => Assert.Equal("99 2\n1 2", Run("""
            from pydantic import BaseModel

            class M(BaseModel):
                a: int
                b: int

            m = M(a=1, b=2)
            m2 = m.copy(update={"a": 99})
            print(m2.a, m2.b)
            print(m.a, m.b)
            """));

    [Fact]
    public void Dict_exclude_and_include_filter_real_fields()
        // Real gap: `.dict(exclude={"b"})` internally does `isinstance(items, AbstractSet)` then
        // `dict.fromkeys(items, ...)` on the real `{"b"}` set literal — both raised before this
        // round (a bad isinstance() result led to pydantic's own "unexpected type" error; fixing
        // that then surfaced dict.fromkeys itself not existing at all).
        => Assert.Equal("{'a': 1, 'c': 3}\n{'a': 1}", Run("""
            from pydantic import BaseModel

            class M(BaseModel):
                a: int
                b: int
                c: int

            m = M(a=1, b=2, c=3)
            print(m.dict(exclude={"b"}))
            print(m.dict(include={"a"}))
            """));

    [Fact]
    public void Validation_error_reports_real_loc_and_type_for_each_bad_field()
        => Assert.Equal("('age',)\ntype_error.integer", Run("""
            from pydantic import BaseModel, ValidationError

            class M(BaseModel):
                age: int

            try:
                M(age="not a number")
            except ValidationError as e:
                err = e.errors()[0]
                print(err["loc"])
                print(err["type"])
            """));

    [Fact]
    public void Json_schema_generation_reflects_real_field_types_and_defaults()
        // Real gap: `.schema()` calls `inspect.getdoc(model)` internally, which didn't exist.
        => Assert.Equal("integer\ndefault", Run("""
            from pydantic import BaseModel

            class M(BaseModel):
                a: int
                b: str = "default"

            schema = M.schema()
            print(schema["properties"]["a"]["type"])
            print(schema["properties"]["b"]["default"])
            """));

    [Fact]
    public void Str_to_int_coercion_works_inside_a_List_int_field()
        => Assert.Equal("[1, 2, 3]", Run("""
            from pydantic import BaseModel
            from typing import List

            class M(BaseModel):
                nums: List[int]

            print(M(nums=["1", "2", "3"]).nums)
            """));
}
