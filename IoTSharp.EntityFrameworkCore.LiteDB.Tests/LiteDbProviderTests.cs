using LiteDB;
using Microsoft.EntityFrameworkCore;
using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;
using Xunit;
using IoTSharp.EntityFrameworkCore.Storage;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class LiteDbProviderTests
{
    [Fact]
    public void SaveChangesAndQueriesUseLiteDbStorage()
    {
        using var database = new TestDatabase();

        using (var context = database.CreateContext())
        {
            Assert.True(context.Database.EnsureCreated());

            var user = new User { Name = "Ada" };
            context.Users.Add(user);
            context.Posts.AddRange(
                new Post { Title = "EF Core", User = user, Score = 10 },
                new Post { Title = "LiteDB", User = user, Score = 20 });

            Assert.Equal(3, context.SaveChanges());
            Assert.NotEqual(0, user.Id);
        }

        using (var context = database.CreateContext())
        {
            var result = context.Posts
                .Where(post => post.Score >= 10)
                .OrderByDescending(post => post.Score)
                .Select(post => new { post.Title, post.Score })
                .ToList();

            Assert.Equal(new[] { "LiteDB", "EF Core" }, result.Select(post => post.Title));
            Assert.Equal(new[] { 20, 10 }, result.Select(post => post.Score));
        }

        database.Close();
        using var liteDb = new LiteDatabase(database.Path);
        var postCollection = "ef_" + typeof(Post).FullName!.Replace('.', '_').Replace('+', '_');
        Assert.Contains(postCollection, liteDb.GetCollectionNames());
        Assert.Equal(2, liteDb.GetCollection<BsonDocument>(postCollection).Count());
    }

    [Fact]
    public async Task AsyncQueriesAndSaveChangesUseTheSameStore()
    {
        using var database = new TestDatabase();

        await using (var context = database.CreateContext())
        {
            context.Users.Add(new User { Name = "Grace" });
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var names = await context.Users
                .AsNoTracking()
                .OrderBy(user => user.Name)
                .Select(user => user.Name)
                .ToListAsync();

            Assert.Equal(new[] { "Grace" }, names);
        }
    }

    [Fact]
    public void UpdateAndDeleteArePersistedAcrossContexts()
    {
        using var database = new TestDatabase();
        int userId;

        using (var context = database.CreateContext())
        {
            var user = new User { Name = "Old name" };
            context.Users.Add(user);
            context.SaveChanges();
            userId = user.Id;
        }

        using (var context = database.CreateContext())
        {
            var user = context.Users.Single(user => user.Id == userId);
            user.Name = "New name";
            context.SaveChanges();
        }

        using (var context = database.CreateContext())
        {
            Assert.Equal("New name", context.Users.Where(user => user.Id == userId).Select(user => user.Name).Single());
            context.Users.Remove(context.Users.Single(user => user.Id == userId));
            context.SaveChanges();
        }

        using (var context = database.CreateContext())
        {
            Assert.False(context.Users.Any());
        }
    }

    [Fact]
    public void EnsureDeletedClearsLiteDbCollections()
    {
        using var database = new TestDatabase();

        using (var context = database.CreateContext())
        {
            context.Users.Add(new User { Name = "Temporary" });
            context.SaveChanges();
            Assert.True(context.Database.EnsureDeleted());
        }

        using (var context = database.CreateContext())
        {
            Assert.False(context.Users.Any());
        }
    }

    private sealed class TestDatabase : IDisposable
    {
        private LiteDBDatabaseRoot _root = new();
        public TestDatabase()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "iotsharp-litedb-tests",
                $"{Guid.NewGuid():N}.db");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        }

        public string Path { get; }

        public TestContext CreateContext() => new(Path, _root);

        public void Close()
        {
            _root.Dispose();
            _root = new();
        }

        public void Dispose()
        {
            _root.Dispose();

            foreach (var file in Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(Path)!, System.IO.Path.GetFileName(Path) + "*"))
            {
                File.Delete(file);
            }
        }
    }

    private sealed class TestContext : DbContext
    {
        private readonly string _path;
        private readonly LiteDBDatabaseRoot _root;

        public TestContext(string path, LiteDBDatabaseRoot root) { _path = path; _root = root; }

        public DbSet<User> Users => Set<User>();
        public DbSet<Post> Posts => Set<Post>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseLiteDB(_path, _root);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(user => user.Id);
            modelBuilder.Entity<Post>().HasKey(post => post.Id);
            modelBuilder.Entity<Post>().HasOne(post => post.User).WithMany(user => user.Posts).HasForeignKey(post => post.UserId);
        }
    }

    private sealed class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Post> Posts { get; } = new();
    }

    private sealed class Post
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Score { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
    }
}
