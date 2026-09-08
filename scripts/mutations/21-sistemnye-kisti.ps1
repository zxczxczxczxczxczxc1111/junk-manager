# Перебивка системных кистей Windows и кольцо фокуса
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.
#
# Класс дефектов тут ровно один и он тихий: пропущенный системный ключ ничего
# не ломает при сборке и не виден на скриншоте счастливого пути. Он всплывает
# синим прямоугольником Windows на выделении, наведении или фокусе, то есть
# ровно в том месте, где полностью переписанный ControlTemplate уже не спасает.
#
# Метка `kisti:` в начале имени нужна для выборочного прогона:
#   powershell -ExecutionPolicy Bypass -File scripts\mutate.ps1 -Tolko 'kisti:'
# Метка латиницей намеренно: аргумент едет через консоль, и кириллица в нём
# зависит от кодовой страницы, а метка обязана совпасть побайтно.

@(
    @{ Imya = 'kisti: выделение красится линией вместо акцента'
       Fayl = 'src/JunkManager.App/Theme/SystemOverrides.xaml'
       Iz   = 'Color="{StaticResource AccentColor}"'
       V    = 'Color="{StaticResource LineMidColor}"' }

    @{ Imya = 'kisti: ключ погашенного текста не перебит'
       Fayl = 'src/JunkManager.App/Theme/SystemOverrides.xaml'
       Iz   = 'x:Key="{x:Static SystemColors.GrayTextBrushKey}"'
       V    = 'x:Key="GrayTextBrush"' }

    @{ Imya = 'kisti: цвет выделения не перебит, перебита только кисть'
       Fayl = 'src/JunkManager.App/Theme/SystemOverrides.xaml'
       Iz   = 'x:Key="{x:Static SystemColors.HighlightColorKey}"'
       V    = 'x:Key="HighlightColor"' }

    @{ Imya = 'kisti: ключ неактивного выделения не перебит'
       Fayl = 'src/JunkManager.App/Theme/SystemOverrides.xaml'
       Iz   = 'x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}"'
       V    = 'x:Key="InactiveSelectionHighlightBrush"' }

    @{ Imya = 'kisti: ключ подсветки пункта меню не перебит'
       Fayl = 'src/JunkManager.App/Theme/SystemOverrides.xaml'
       Iz   = 'x:Key="{x:Static SystemColors.MenuHighlightBrushKey}"'
       V    = 'x:Key="MenuHighlightBrush"' }

    @{ Imya = 'kisti: глобальный крючок кольца фокуса снят, возвращается пунктир системы'
       Fayl = 'src/JunkManager.App/Theme/SystemOverrides.xaml'
       Iz   = 'x:Key="{x:Static SystemParameters.FocusVisualStyleKey}"'
       V    = 'x:Key="SistemnoeKolcoFokusa"' }

    @{ Imya = 'kisti: своё кольцо фокуса стало пунктирным'
       Fayl = 'src/JunkManager.App/Theme/SystemOverrides.xaml'
       Iz   = 'StrokeThickness="2"'
       V    = 'StrokeThickness="2" StrokeDashArray="1 2"' }

    @{ Imya = 'kisti: кольцо фокуса красится линией вместо акцента'
       Fayl = 'src/JunkManager.App/Theme/SystemOverrides.xaml'
       Iz   = 'Stroke="{StaticResource AccentBrush}"'
       V    = 'Stroke="{StaticResource Ink3Brush}"' }
)
