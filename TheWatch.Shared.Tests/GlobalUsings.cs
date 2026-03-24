// WAL: Global using directives for xUnit test framework.
// Without this, [Fact], [Theory], [InlineData], and Assert won't resolve.
//
// Example — xUnit test:
//   public class MyTests
//   {
//       [Fact]
//       public void ShouldPass() => Assert.True(true);
//
//       [Theory]
//       [InlineData(1, 2, 3)]
//       public void ShouldAdd(int a, int b, int expected)
//           => Assert.Equal(expected, a + b);
//   }

global using Xunit;
