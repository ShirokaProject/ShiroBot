# Rendering experiment fonts

Noto Sans SC subsets generated from Google Fonts' NotoSansSC[wght].ttf under the accompanying OFL.txt license. Static weights: 400, 500, 600, 700, 800, 900. The existing Noto Sans SC family name is preserved; the accompanying OFL file reserves the name Source.

These files cover only fixed rendering test text. They must not be shipped as production fonts for arbitrary user content. No system fonts were modified.

For the 0.1.13 retest, the Google Fonts variable TrueType source was subset with fontTools and instantiated at the six weights listed above. The source SHA-256 is `a3041811a78c361b1de50f953c805e0244951c21c5bd412f7232ef0d899af0da`. The expanded fixture suite adds characters from the four plugins’ card sources and benchmark models. Each Noto benchmark run records the hashes of the six selected faces in `samples.json`.

Original: https://github.com/google/fonts/tree/main/ofl/notosanssc
