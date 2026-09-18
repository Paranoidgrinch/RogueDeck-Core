using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// A builder can start from a registry that is already built and add to it. This is what lets a game compile
/// its authored library ONCE and assemble every fight on top of it, instead of compiling and re-validating the
/// same few thousand definitions for every single fight.
///
/// What is tested here is the promise that makes that safe: everything the base held is still there, the same
/// instances, and everything added afterwards is still checked.
/// </summary>
public class RegistryBuiltOnARegistryTests
{
    [Fact]
    public void Everything_the_base_held_is_in_the_registry_built_on_it()
    {
        var baseBuilder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(baseBuilder);
        baseBuilder.RegisterCard(Card("library.smite"));
        baseBuilder.RegisterStatus(Status("library.rooted"));
        var library = baseBuilder.Build();

        var registry = new CombatDefinitionRegistryBuilder(library).Build();

        Assert.True(registry.TryGetCard(new CardDefinitionId("library.smite"), out _));
        Assert.True(registry.TryGetStatus(new StatusDefinitionId("library.rooted"), out _));
        // A standard-package status too, so this covers what the package registered and not only what the
        // library added on top of it.
        Assert.True(registry.TryGetStatus(StandardCombatIds.PoisonStatus, out _));
    }

    [Fact]
    public void A_definition_is_the_same_instance_in_the_base_and_in_what_is_built_on_it()
    {
        // The point of a shared library is that it is SHARED: two fights built on one library address one set
        // of definitions. This is how the engine already behaved within a fight — definitions are read-only
        // after Build() — and it is what makes the sharing sound rather than merely cheap.
        var baseBuilder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(baseBuilder);
        baseBuilder.RegisterCard(Card("library.smite"));
        var library = baseBuilder.Build();

        var first = new CombatDefinitionRegistryBuilder(library).Build();
        var second = new CombatDefinitionRegistryBuilder(library).Build();

        Assert.Same(
            library.GetCard(new CardDefinitionId("library.smite")),
            first.GetCard(new CardDefinitionId("library.smite")));
        Assert.Same(
            first.GetCard(new CardDefinitionId("library.smite")),
            second.GetCard(new CardDefinitionId("library.smite")));
    }

    [Fact]
    public void What_is_added_on_top_of_a_base_is_still_validated()
    {
        // The base's definitions are not walked again — they were walked when the base was built. What comes
        // afterwards has never been checked, so it must be, or a library would become a way to smuggle a
        // broken program past the build.
        var baseBuilder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(baseBuilder);
        var library = baseBuilder.Build();

        var builder = new CombatDefinitionRegistryBuilder(library);
        var card = Card("fight.broken");
        card.Program = new EffectProgram<CardPlayContext>(new NodeWithNoExecutor());
        builder.RegisterCard(card);

        var ex = Assert.Throws<CombatDefinitionBuildException>(() => builder.Build());
        Assert.Contains("no executor", ex.Message);
        Assert.Contains("NodeWithNoExecutor", ex.Message);
    }

    [Fact]
    public void A_base_that_was_built_does_not_stop_its_successor_registering_an_executor()
    {
        // Building SEALS the node-executor registry. A builder started from a built registry therefore needs
        // its own copy of it — otherwise the first custom executor a fight registers would throw "sealed".
        var baseBuilder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(baseBuilder);
        var library = baseBuilder.Build();
        Assert.True(library.EffectNodeExecutors.IsSealed);

        var builder = new CombatDefinitionRegistryBuilder(library);
        builder.RegisterEffectNodeExecutor(typeof(NodeWithNoExecutor), new DoNothingExecutor());
        var card = Card("fight.custom");
        card.Program = new EffectProgram<CardPlayContext>(new NodeWithNoExecutor());
        builder.RegisterCard(card);

        var registry = builder.Build(); // the program validates now: its node has an executor
        Assert.True(registry.EffectNodeExecutors.TryGet(typeof(NodeWithNoExecutor), out _));
        // And the base is untouched: it did not gain the executor its successor registered.
        Assert.False(library.EffectNodeExecutors.TryGet(typeof(NodeWithNoExecutor), out _));
    }

    [Fact]
    public void An_id_the_base_already_holds_cannot_be_registered_again()
    {
        // Duplicate protection reaches across the seam: a fight cannot quietly shadow a library card with a
        // different definition of the same id.
        var baseBuilder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(baseBuilder);
        baseBuilder.RegisterCard(Card("library.smite"));
        var library = baseBuilder.Build();

        var builder = new CombatDefinitionRegistryBuilder(library);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.RegisterCard(Card("library.smite")));
        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public void An_interceptor_added_on_top_takes_its_place_among_the_bases_rather_than_after_them()
    {
        // Interceptors are consulted in one order — priority first, then id — and they are consulted in the
        // order the built registry holds them. So merging is not appending: a successor's interceptor with a
        // lower priority has to land BEFORE the base's, exactly as it would have if everything had been
        // registered into one builder.
        var baseBuilder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(baseBuilder);
        baseBuilder.RegisterPreDownInterceptor(new NamedPreDownInterceptor("second", priority: 10));
        baseBuilder.RegisterPreDownInterceptor(new NamedPreDownInterceptor("first", priority: 5));
        var library = baseBuilder.Build();

        var builder = new CombatDefinitionRegistryBuilder(library);
        builder.RegisterPreDownInterceptor(new NamedPreDownInterceptor("earliest", priority: 1));
        var registry = builder.Build();

        Assert.Equal(
            ["earliest", "first", "second"],
            registry.GetPreDownInterceptors().OfType<NamedPreDownInterceptor>().Select(i => i.Name));
    }

    private static CardDefinitionBuilder Card(string id) =>
        new(new CardDefinitionId(id), new PackageId("test"), $"card.{id}.name", $"card.{id}.desc");

    private static StatusDefinition Status(string id) =>
        new(new StatusDefinitionId(id), new PackageId("test"), $"status.{id}.name", $"status.{id}.desc");

    private sealed class NodeWithNoExecutor : IEffectNode<CardPlayContext>
    {
        public IReadOnlyList<IEffectNode<CardPlayContext>> Children => [];
    }

    private sealed class DoNothingExecutor : IEffectNodeExecutor
    {
        public void Execute(
            IEffectNode node, IEffectExecutionContextCore ctx, CombatState combat,
            Action<CombatState>? onComplete,
            Action<IEffectNode, CombatState, Action<CombatState>?> dispatch) =>
            onComplete?.Invoke(combat);
    }

    // Lets nothing through differently — it exists only to be recognisable in the built order.
    private sealed class NamedPreDownInterceptor(string name, int priority = 0) : IPreDownInterceptor
    {
        public string Name { get; } = name;

        public string InterceptorId => Name;

        public int Priority { get; } = priority;

        public PreDownInterceptionResult Intercept(PreDownInterceptionContext context) =>
            PreDownInterceptionResult.Allow;
    }
}
