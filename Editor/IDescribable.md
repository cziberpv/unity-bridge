# IDescribable — Cookbook

Виджет это не код. Это место, предмет, существо — что-то, с чем AI-агент встретится и, возможно, запомнит.

## Quick Start

```csharp
public class Torch : MonoBehaviour, IDescribable
{
    private bool lit = true;

    public ScreenFragment Describe() => new ScreenFragment
    {
        Name = "Wall Torch",
        Description = lit ? "Flames dance, casting warm shadows." : "A dead torch. The wall is cold here.",
        Actions = lit
            ? new[] { GameAction.Create("Extinguish", () => { lit = false; return "The flame hisses and dies."; }) }
            : null
    };
}
```

Добавь на GameObject, вызови `describe` — виджет на экране.

## Widget Lifecycle

Виджет живёт в одном из трёх состояний. Все три — валидный дизайн.

**Active** — есть actions, агент взаимодействует:
```
Torch — Flames dance, casting warm shadows.
  [interact] /Torch/Extinguish
```

**Decoration** — actions исчезли, виджет остался как часть мира:
```
Torch — A dead torch. The wall is cold here.
```

**Gone** — виджет убрал `IDescribable` или деактивировал GameObject. Исчез с радара полностью.

Выбирай под гейм-дизайн. Сундук отдал золото и стал декорацией. Монстр убит и исчез. Дверь открыта, но через неё можно пройти — active.

## Text Quality

Текст — это не метаданные. Это единственное, что агент видит. Это и есть мир.

| Плохо | Хорошо |
|-------|--------|
| `Lever (state: not_pulled)` | `A rusty lever protruding from the wall.` |
| `Door. Status: locked` | `A heavy wooden door with iron lock mechanism. Locked.` |
| `Open` | `Reveal what's inside` |

**Name** — идентичность. "Treasure Chest", не "chest_01".

**Description** — присутствие. Текстура, температура, звук. Агент должен *почувствовать* предмет.

**Action hint** — приглашение, не команда. "Step into the light" > "Use door". "See what happens" > "Activate".

## Disabled Actions

Два паттерна — оба нужны.

**Teach** — подсказка, обучение:
```csharp
GameAction.Disabled("Open", "The door is locked. You hear mechanism inside — something must unlock it.")
```
Рендерится как:
```
[interact] /Door/Open — disabled: "The door is locked. You hear mechanism inside..."
```
Агент понял что делать. Это accessibility и нарратив одновременно.

**Challenge** — серая кнопка, разбирайся сам:
```csharp
GameAction.Disabled("Buy", null)
```
Рендерится как:
```
[interact] /Shop/Buy — disabled
```
Без объяснений. Агент видит что action существует, но недоступен. Пусть исследует мир.

## Cross-Widget Dependencies

Протокол не знает о связях между виджетами. Это обычная Unity-логика:

```csharp
public class TreasureDoor : MonoBehaviour, IDescribable
{
    [SerializeField] private TreasureLever lever;

    public ScreenFragment Describe()
    {
        bool unlocked = lever != null && lever.IsPulled;
        // ...действия зависят от состояния lever
    }
}
```

`FindObjectOfType`, `[SerializeField]`, события, `ScriptableObject` — любой способ. Когда агент вызовет `describe`, каждый виджет вернёт свою актуальную правду. Lever потянут → Door разблокирована. Магия — в обычном коде.

## Semantic Naming

**Name виджета** = его имя в мире. "Treasure Chest", "Ancient Lever", "Iron Door". Не "chest_01", не "interactable_3".

**Action.Id** = глагол. "Open", "Pull", "GoThrough". Не "activate", не "use", не "action1". Id становится частью пути:

```
/Chest/Open          — понятно
/Door/GoThrough      — читается как действие
/Lever/Pull          — точно и коротко
```

Путь читается как нарратив: `/Dock/Ship/Slot[1,1]/Equip` — я на причале, на корабле, в слоте 1,1 экипирую что-то.

## Acceptance Criterion

> Пришёл как тестер, ушёл как игрок.

Если AI-агент чувствует что выполняет тест-кейсы — виджет написан плохо. Описания сухие, действия механические, мир молчит.

Если граница между тестированием и игрой стёрлась — виджет работает. Агент не "проверяет реакцию Open на Chest", а *открывает сундук и видит золото*.

Из playtest-отчёта:

> *"The lever resists, then gives way with a satisfying clunk. Something unlocked in the distance."*
>
> This sentence made me FEEL the lever. Not see it, FEEL it.

Это критерий. Не покрытие API, а присутствие.
