# Персонажи Calabash: концепт в манга-туши

Процедурные концепт-модели по `docs/characters` в том же стиле, что и деревня (`village/`).
Безликие фигуры, глаза — белые точки, вытянутые сутулые тела. Цвет есть только у огня
(оранжевый: Ждан), воды (голубой: тряпка и ведро) и крови (красный: фартук палача).

| Файл | Что это |
|---|---|
| `scripts/build_characters.py` | Blender 4.5: строит 16 персонажей, сохраняет `characters.blend`, `fbx/SM_Char_<id>.fbx` и прозрачные вырезки `renders/raw/<id>.png`. С флагом `--corridor` строит сцену в коридоре: `corridor.blend` и `renders/raw/corridor.png` |
| `scripts/compose_sheets.py` | python3 + Pillow: карточки `renders/char_<id>.png`, лайнап `renders/characters_lineup.png`, проверка силуэта на 64 px, финальный кадр `renders/corridor.png` |

```bash
blender -b --factory-startup -P characters/scripts/build_characters.py
blender -b --factory-startup -P characters/scripts/build_characters.py -- --corridor
python3 characters/scripts/compose_sheets.py
```

`--only=zhdan0,shadow` рендерит только этих персонажей, но **перезаписывает** `characters.blend`
и FBX неполным набором. Для финального прогона запускайте без него.

Ростер:
- Ждан, стадии 0, 2 и 4;
- Тень (огонь в мире);
- фигура Лимбо;
- палач Корней;
- Король;
- враги: крестьянин, крестьянин с мокрой тряпкой, стража, стража с ведром, лучник;
- жертвы: Гаврила (№1), Агафья (№5), Никодим (№8), Ефим (№18).

Это статичные меши без рига: они нужны как референс позы, силуэта и пропорций для 2D-спрайтов.
Правила ассета:
- единицы — метры;
- перед смотрит в −Y;
- пивот у ног.

Глаза и угли вынесены в отдельный объект `_Glow`: у него нет контурных линий.
