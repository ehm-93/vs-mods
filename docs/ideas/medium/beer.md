# Medieval Beer Brewing

Real beer brewing is an in-depth multi-step process with many ingredients, not just flour in a barrel.

## Design Philosophy

Balance IRL brewing against game mechanics. Reward more effort and investment with more nutrition or other effects.

**Compatibility goal:** Build on or alongside A Culinary Artillery / Expanded Foods where possible. Reuse their bottles, cauldrons, barrel fermentation patterns, and yeast starter. Avoid reinventing wheels.

---

## Process Overview

### 1. Malting

Convert raw grain into malt by sprouting and kilning.

**Requirements**
- Grain
- Barrel + Water
- Oven + Fuel

**Outputs**
- Malt (or Dark Malt)

**Steps:**
1. Soak grain in water (barrel) for 1-2 days → steeped grain
2. Spread steeped grain on ground to sprout (needs appropriate ambient temp)
3. Cook in oven to stop sprouting → malt
4. Optional: leave in oven longer → dark malt (modestly reduced nutrition and longer shelf life)

---

### 2. Mashing

Extract sugars from malt into wort.

**Requirements**
- Malt
- Quern
- Cauldron + Fuel + Water

**Outputs**
- Wort

**Steps:**
1. Crush malt in quern → grist
2. Cook grist + water in cauldron → wort + weak grist
3. Optional: cook weak grist + water → wort + spent grist
4. Optional: cook spent grist + water → wort (grist exhausted)

Lower quality grist yields less wort per unit. Three runnings from one batch of malt.

**Yield:** Vanilla VS produces 0.2L ale per grain. With three runnings, target ~0.4L total wort per grain. More effort, more output.

---

### 3. Infusion (Optional)

Add flavor, nutrition, or other effects to wort.

**Requirements**
- Wort
- Cauldron + Fuel
- Infusion ingredient

**Outputs**
- Infused Wort

**Infusion options:**
| Ingredient | Effect |
|------------|--------|
| Horsetail | Healing |
| Fruit/Honey | Extra nutrition |
| Hops | Extended shelf life |

**Steps:**
1. Cook wort + infusion in cauldron → infused wort

---

### 4. Fermentation

Convert wort to alcohol.

**Requirements**
- Wort (or Infused Wort)
- Yeast starter (from ACA bread making)
- Barrel

**Outputs**
- Flat Ale

**Steps:**
1. Mix yeast starter with wort → yeasted wort
2. Seal yeasted wort in barrel
3. Wait 1 game month → flat ale

**Note**
Flat ale can be distilled like vanilla ale

---

### 5. Lagering (Optional)

Cold-condition ale for a smoother, more nutritious result.

**Requirements**
- Flat Ale
- Barrel
- Cellar below ~5°C / 41°F

**Outputs**
- Lager (+15% nutrition, +15% infusion bonuses, longer shelf life)

**Steps:**
1. Place barrel of flat ale in cold cellar
2. Wait 2 game months
3. If temp stays below threshold → lager
4. If temp rises → stays ale (no penalty, just no bonus)

**Implementation note:** Implement as a curing/aging process like ACA's alcohol aging, not a barrel recipe. That system handles time-based transformation and supports ambient temperature checks.

---

### 6. Bottling (Optional)

Carbonate for bonus nutrition and shelf life.

**Requirements**
- Flat Ale or Lager
- Glass bottle + Cork

**Outputs**
- Ale or Lager (+10% nutrition and infusion bonus, longer shelf life)

**Steps:**
1. Transfer flat ale/lager to bottle
2. Cork bottle
3. Wait 1 game month → carbonated

---

## Summary

| Stage | Time | Output | Cumulative Bonus |
|-------|------|--------|------------------|
| Fermentation | 1 month | Flat Ale | Base |
| + Infusion | — | Flat Ale | + effect (healing/nutrition/shelf life) |
| + Lagering | +2 months | Lager | +15% nutrition, +15% infusion |
| + Bottling | +1 month | Carbonated | +10% nutrition, shelf life |

Max investment path: Malt → Mash → Infuse → Ferment → Lager → Bottle = 4 months, best possible beer.

Quick path: Malt → Mash → Ferment = 1 month, basic flat ale.
