# Экран обзора: полоса тома и категории
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'obzor: найденное становится добавкой к занятому, а не его частью'
       Fayl = 'src/JunkManager.App/Controls/DiskStrip.cs'
       Iz   = 'var procheeBayt = zanyatoBezVyhoda - naydenoBayt;'
       V    = 'var procheeBayt = zanyatoBezVyhoda;' }

    @{ Imya = 'obzor: найденное перестаёт ограничиваться занятым'
       Fayl = 'src/JunkManager.App/Controls/DiskStrip.cs'
       Iz   = 'var naydenoBayt = Math.Clamp(naydeno, 0, zanyatoBezVyhoda);'
       V    = 'var naydenoBayt = naydeno;' }

    @{ Imya = 'obzor: тонкий сегмент округляется до нуля'
       Fayl = 'src/JunkManager.App/Controls/DiskStrip.cs'
       Iz   = 'if (doli[i] > 0 && shiriny[i] < MinimalnayaShirina)'
       V    = 'if (false && doli[i] > 0 && shiriny[i] < MinimalnayaShirina)' }

    @{ Imya = 'obzor: минимальная ширина даётся и нулевой доле'
       Fayl = 'src/JunkManager.App/Controls/DiskStrip.cs'
       Iz   = 'if (doli[i] > 0 && shiriny[i] < MinimalnayaShirina)'
       V    = 'if (shiriny[i] < MinimalnayaShirina)' }

    @{ Imya = 'obzor: хвост округления не докладывается и справа остаётся щель'
       Fayl = 'src/JunkManager.App/Controls/DiskStrip.cs'
       Iz   = 'if (summa > 0 && Math.Abs(summa - shirina) > 0.01)'
       V    = 'if (false && summa > 0 && Math.Abs(summa - shirina) > 0.01)' }

    @{ Imya = 'obzor: остаток перестаёт быть последним в списке категорий'
       Fayl = 'src/JunkManager.App/ViewModels/CategoryViewModel.cs'
       Iz   = 'itog.Add(Sobrat(OstatokName, ostatok));'
       V    = 'itog.Insert(0, Sobrat(OstatokName, ostatok));' }

    @{ Imya = 'obzor: одна мелкая категория снова сворачивается в Прочее'
       Fayl = 'src/JunkManager.App/ViewModels/CategoryViewModel.cs'
       Iz   = 'if (ostatok.Count > 0 && syrye.Count - krupnye.Count == 1)'
       V    = 'if (false && ostatok.Count > 0 && syrye.Count - krupnye.Count == 1)' }

    @{ Imya = 'obzor: прерванный проход выдаётся за чистую машину'
       Fayl = 'src/JunkManager.App/ViewModels/OverviewViewModel.cs'
       Iz   = 'if (Categories.Count == 0 && !itog.Cancelled)'
       V    = 'if (Categories.Count == 0)' }

    @{ Imya = 'obzor: доля риска делится на ноль вместо честного нуля'
       Fayl = 'src/JunkManager.App/ViewModels/CategoryViewModel.cs'
       Iz   = 'public double RiskShare => TotalBytes > 0 ? (double)RiskBytes / TotalBytes : 0;'
       V    = 'public double RiskShare => (double)RiskBytes / Math.Max(1, TotalBytes) + 0.5;' }

    @{ Imya = 'obzor: опасное отмечается к удалению заранее'
       Fayl = 'src/JunkManager.App/ViewModels/CategoryViewModel.cs'
       Iz   = 'IsSelected = f.Tier == RiskTier.Safe,'
       V    = 'IsSelected = true,' }
)
