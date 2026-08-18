using System.Runtime.CompilerServices;
using GoHome.Core;

namespace GoHome.Tests;

/// <summary>
/// Слепок расчёта на наборе журналов из <see cref="RulesCorpus"/>. Снят до того, как версия
/// правил уступила место снимку настроек в файле дня, и с тех пор не должен меняться:
/// он и есть доказательство, что старые файлы считаются ровно как раньше.
/// </summary>
/// <remarks>
/// Файл слепка лежит рядом с тестом и правится только осознанно. Если расчёт изменился
/// намеренно, слепок пересоздаётся удалением файла и одним прогоном — и тогда разница
/// видна в диффе построчно, а не теряется.
/// </remarks>
public sealed class RulesBaselineTests
{
    [Fact]
    public void Расчёт_на_старых_файлах_не_изменился()
    {
        var lines = RulesCorpus.All()
            .Select(entry => RulesCorpus.Render(
                entry.Name,
                WorkTimeCalculator.Compute(entry.Log, RulesCorpus.Now)))
            .ToList();

        var path = BaselinePath();
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines);
            Assert.Fail($"Слепок расчёта создан заново: {path}. Проверьте его глазами и закоммитьте.");
        }

        Assert.Equal(File.ReadAllLines(path), lines);
    }

    /// <summary>Путь к слепку через путь исходника: тест не зависит от копирования в выходной каталог.</summary>
    private static string BaselinePath([CallerFilePath] string caller = "") =>
        Path.Combine(Path.GetDirectoryName(caller)!, "Fixtures", "rules-baseline.txt");
}