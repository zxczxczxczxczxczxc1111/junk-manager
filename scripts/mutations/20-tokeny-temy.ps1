# Токены темы: цвета, шкала риска, лестница поверхностей
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.
#
# Мутации тут правят РАЗМЕТКУ, а не код. Компилятор их не ловит по определению:
# `#FFE0A45F` и `#FFA88BE0` для него одинаково валидные строки. Ровно поэтому
# ворота контраста считаются тестом, а не глазами на макете.
#
# Метка `tokeny:` в начале имени нужна для выборочного прогона:
#   powershell -ExecutionPolicy Bypass -File scripts\mutate.ps1 -Tolko 'tokeny:'
# Метка латиницей намеренно: аргумент едет через консоль, и кириллица в нём
# зависит от кодовой страницы, а метка обязана совпасть побайтно.

@(
    @{ Imya = 'tokeny: акцент вернулся к меди и слился с верхней ступенью риска'
       Fayl = 'src/JunkManager.App/Theme/Tokens.xaml'
       Iz   = '<Color x:Key="AccentColor">#FFA88BE0</Color>'
       V    = '<Color x:Key="AccentColor">#FFE0A45F</Color>' }

    @{ Imya = 'tokeny: путь красится декоративными чернилами и тонет на подложке'
       Fayl = 'src/JunkManager.App/Theme/Tokens.xaml'
       Iz   = '<Color x:Key="InkPathColor">#FF7A7A85</Color>'
       V    = '<Color x:Key="InkPathColor">#FF44444C</Color>' }

    @{ Imya = 'tokeny: шкала риска перевёрнута, Safe светлее Risk'
       Fayl = 'src/JunkManager.App/Theme/Tokens.xaml'
       Iz   = '<Color x:Key="SafeInkColor">#FFB08A6B</Color>'
       V    = '<Color x:Key="SafeInkColor">#FFEFD8C4</Color>' }

    @{ Imya = 'tokeny: заливка безвозвратного удаления светлая, белое на ней 3.92'
       Fayl = 'src/JunkManager.App/Theme/Tokens.xaml'
       Iz   = '<Color x:Key="DangerFillColor">#FFC93A3F</Color>'
       V    = '<Color x:Key="DangerFillColor">#FFE5484D</Color>' }

    @{ Imya = 'tokeny: вторичные чернила опущены до уровня мелкой метки'
       Fayl = 'src/JunkManager.App/Theme/Tokens.xaml'
       Iz   = '<Color x:Key="Ink2Color">#FF9E9EA8</Color>'
       V    = '<Color x:Key="Ink2Color">#FF65656E</Color>' }

    @{ Imya = 'tokeny: мелкая метка подтянута до уровня вторичных чернил'
       Fayl = 'src/JunkManager.App/Theme/Tokens.xaml'
       Iz   = '<Color x:Key="Ink3Color">#FF65656E</Color>'
       V    = '<Color x:Key="Ink3Color">#FF9E9EA8</Color>' }

    @{ Imya = 'tokeny: поверхность карточки слиплась с панелью'
       Fayl = 'src/JunkManager.App/Theme/Tokens.xaml'
       Iz   = '<Color x:Key="CardColor">#FF141418</Color>'
       V    = '<Color x:Key="CardColor">#FF0E0E11</Color>' }

    @{ Imya = 'tokeny: утопленная поверхность светлее подложки, лестница перевёрнута'
       Fayl = 'src/JunkManager.App/Theme/Tokens.xaml'
       Iz   = '<Color x:Key="SunkenColor">#FF050506</Color>'
       V    = '<Color x:Key="SunkenColor">#FF121215</Color>' }

    @{ Imya = 'tokeny: ступень Safe уехала в зелёный, оттенок шкалы перестал быть одним'
       Fayl = 'src/JunkManager.App/Theme/Tokens.xaml'
       Iz   = '<Color x:Key="SafeFillColor">#FF6B4A32</Color>'
       V    = '<Color x:Key="SafeFillColor">#FF326B4A</Color>' }
)
