using System;
using System.Collections.Generic;
using System.Text;
using Game.Battle.Model.Contests;
using Game.Flow;
using Game.Foundation.Math;
using Game.Unity.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.Screens
{
    /// <summary>
    /// 전투 직전 대결 (일기토·논쟁, battle.md 2.7, 2.7.8). 대전 게임처럼:
    /// 위: 좌우 장수 이름·체력 게이지·기운 게이지, 가운데 라운드·3·2·1 카운트.
    /// 무대: 두 장수가 좌우에 마주 서고, 공개되면 고른 행동을 한다 (공격 돌진·회피 뒷걸음·기운 모으기 기운·활쏘기 화살·퇴각 물러남·필살 붉은 돌진).
    /// 맞으면 흔들리고 피해 숫자(회심은 크게), 기운이 오르면 +%. 끝나면 쓰러지거나 물러나고, 결과와 전투 영향을 보여준다.
    /// 논쟁은 같은 무대에서 말(말풍선)로 겨룬다. 계산은 규칙 쪽(<see cref="ContestPlay"/>), 고른 기록이 전투에 그대로 들어간다.
    /// 장수 모습은 코드로 만든 임시 인형 (그림은 아트 단계에서 바꾼다).
    /// </summary>
    public sealed class ContestScreen : GameScreen
    {
        private const float ChooseSeconds = 3f;   // 설계 2.7.1: 3·2·1
        private const float RevealSeconds = 2.4f; // 행동 + 맞음 연출
        private const float Dash = 150f;

        private enum Phase { Choosing, Reveal, Over }

        private readonly SortiePlan _plan;
        private readonly int _index;
        private ContestPlay _play;
        private Phase _phase;
        private float _left;
        private string _pending;
        private bool _debate;

        // 위 표시
        private Text _nameA, _nameB, _valuesA, _valuesB, _round, _count;
        private RectTransform _hpA, _hpB, _chA, _chB;
        // 무대
        private RectTransform _stage;
        private Puppet _a, _b;
        private Text _banner;
        private Text _detail, _effect;
        // 아래
        private RectTransform _actions;
        private readonly Dictionary<string, Button> _actionButtons = new Dictionary<string, Button>();
        private Button _autoButton, _nextButton;

        // 짧은 움직임 (Tick에서 돈다)
        private float _clock;
        private readonly List<Tween> _tweens = new List<Tween>();
        private sealed class Tween { public float Start, Duration; public Action<float> Step; public Action Done; }

        public ContestScreen(SortiePlan plan, int index) { _plan = plan; _index = index; }

        public override void Build()
        {
            _play = new ContestPlay(_plan.Setup, _index);
            _debate = _play.Def.Id == "debate";
            Ui.Column(Root, 10, 20);
            Ui.Size(Ui.Label(Root, T("ui.contest." + _play.Def.Id), 52, TextAnchor.MiddleCenter), 70);
            BuildHud();
            BuildStage();
            _detail = Ui.Label(Root, "", 28, TextAnchor.MiddleCenter);
            Ui.Size(_detail, 80);

            _actions = Ui.Rect(Root, "Actions");
            var grid = _actions.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(320, 100);
            grid.spacing = new Vector2(12, 12);
            grid.childAlignment = TextAnchor.MiddleCenter;
            Ui.Size(_actions, 220);

            var row = Ui.Row(Root, "Buttons", Ui.ButtonHeight);
            _autoButton = Ui.Button(row, T("ui.contest.auto"), () => { _play.AutoFinish(); ShowOver(); });
            _nextButton = Ui.Button(row, T("ui.contest.next"), Next, color: Palette.ButtonActive);
            BeginChoosing();
        }

        // ---------------- 위: 게이지 ----------------

        private void BuildHud()
        {
            var hud = Ui.Row(Root, "Hud", 170, 10);
            var left = Ui.Card(hud, "A", 12, 4);
            Ui.Size(left, flexibleWidth: 1);
            _nameA = Ui.Label(left, Ui.Colored(Ctx.GeneralName(GeneralOf(_play.Setup.CorpsA)), Palette.Ally), 30);
            _hpA = Gauge(left, Palette.Good, 30, false);
            _chA = Gauge(left, new Color(0.98f, 0.8f, 0.25f), 16, false);
            _valuesA = Ui.Label(left, "", 22, TextAnchor.MiddleLeft, Palette.TextDim);

            var mid = Ui.Rect(hud, "Mid");
            Ui.Size(mid, width: 200);
            Ui.Column(mid, 0);
            _round = Ui.Label(mid, "", 24, TextAnchor.MiddleCenter, Palette.TextDim);
            Ui.Size(_round, 40);
            _count = Ui.Label(mid, "", 84, TextAnchor.MiddleCenter);
            Ui.Size(_count, 120);

            var right = Ui.Card(hud, "B", 12, 4);
            Ui.Size(right, flexibleWidth: 1);
            _nameB = Ui.Label(right, Ui.Colored(Ctx.GeneralName(GeneralOf(_play.Setup.CorpsB)), Palette.Enemy), 30, TextAnchor.MiddleRight);
            _hpB = Gauge(right, Palette.Good, 30, true);
            _chB = Gauge(right, new Color(0.98f, 0.8f, 0.25f), 16, true);
            _valuesB = Ui.Label(right, "", 22, TextAnchor.MiddleRight, Palette.TextDim);
        }

        /// <summary>게이지 (오른쪽 장수는 오른쪽에서 채운다).</summary>
        private static RectTransform Gauge(Transform parent, Color color, float height, bool fromRight)
        {
            var back = Ui.Rect(parent, "Gauge");
            Ui.Background(back, new Color(0f, 0f, 0f, 0.55f));
            Ui.Size(back, height);
            var fill = Ui.Rect(back, "Fill");
            fill.anchorMin = new Vector2(fromRight ? 0f : 0f, 0f);
            fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.pivot = new Vector2(fromRight ? 1f : 0f, 0.5f);
            Ui.Background(fill, color);
            return fill;
        }

        private static void SetGauge(RectTransform fill, float ratio, bool fromRight)
        {
            ratio = Mathf.Clamp01(ratio);
            if (fromRight) { fill.anchorMin = new Vector2(1f - ratio, 0f); fill.anchorMax = Vector2.one; }
            else { fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(ratio, 1f); }
            fill.offsetMin = fill.offsetMax = Vector2.zero;
        }

        private void RefreshGauges()
        {
            var def = _play.Def;
            var rounds = _play.Rounds();
            var last = rounds.Count > 0 ? rounds[rounds.Count - 1] : null;
            Fix ra = last?.ResourceA ?? def.Start, rb = last?.ResourceB ?? def.Start;
            Fix ca = last?.ChargeA ?? Fix.Zero, cb = last?.ChargeB ?? Fix.Zero;
            float start = Mathf.Max(1f, (float)def.Start.ToDisplayDouble()), full = Mathf.Max(1f, (float)def.SpecialCharge.ToDisplayDouble());
            SetGauge(_hpA, (float)ra.ToDisplayDouble() / start, false);
            SetGauge(_hpB, (float)rb.ToDisplayDouble() / start, true);
            SetGauge(_chA, (float)ca.ToDisplayDouble() / full, false);
            SetGauge(_chB, (float)cb.ToDisplayDouble() / full, true);
            _hpA.GetComponent<Image>().color = (float)ra.ToDisplayDouble() / start <= 0.3f ? Palette.Bad : Palette.Good;
            _hpB.GetComponent<Image>().color = (float)rb.ToDisplayDouble() / start <= 0.3f ? Palette.Bad : Palette.Good;
            _valuesA.text = T(def.ResourceKey) + " " + ra.FloorToLong() + "   " + T(def.ChargeKey) + " " + ca.FloorToLong() + "%" + (ca >= def.SpecialCharge ? "  " + T("ui.contest.full") : "");
            _valuesB.text = (cb >= def.SpecialCharge ? T("ui.contest.full") + "  " : "") + T(def.ResourceKey) + " " + rb.FloorToLong() + "   " + T(def.ChargeKey) + " " + cb.FloorToLong() + "%";
        }

        // ---------------- 무대: 장수 인형 ----------------

        private sealed class Puppet
        {
            public RectTransform Root, Weapon, Aura;
            public Image Body, AuraImage;
            public Text Bubble;
            public float Dir; // 앞으로 = +1 (왼쪽 장수) / -1 (오른쪽 장수)
            public Color Team;
            public float WeaponRest;
        }

        private void BuildStage()
        {
            _stage = Ui.Rect(Root, "Stage");
            Ui.Background(_stage, new Color(0f, 0f, 0f, 0.25f));
            Ui.Size(_stage, flexibleHeight: 1);
            var ground = Ui.Rect(_stage, "Ground");
            ground.anchorMin = new Vector2(0f, 0f);
            ground.anchorMax = new Vector2(1f, 0.2f);
            ground.offsetMin = ground.offsetMax = Vector2.zero;
            Ui.Background(ground, new Color(0.35f, 0.3f, 0.22f, 0.5f));
            _a = MakePuppet(0.27f, Palette.Ally, +1f);
            _b = MakePuppet(0.73f, Palette.Enemy, -1f);
            _banner = Ui.Label(_stage, "", 64, TextAnchor.MiddleCenter);
            var br = _banner.rectTransform;
            br.anchorMin = new Vector2(0f, 0.62f);
            br.anchorMax = new Vector2(1f, 0.95f);
            br.offsetMin = br.offsetMax = Vector2.zero;
            _effect = Ui.Label(_stage, "", 28, TextAnchor.LowerCenter, Palette.Text);
            var er = _effect.rectTransform;
            er.anchorMin = new Vector2(0.04f, 0.02f);
            er.anchorMax = new Vector2(0.96f, 0.2f);
            er.offsetMin = er.offsetMax = Vector2.zero;
        }

        private static RectTransform Part(Transform parent, string name, Vector2 pos, Vector2 size, Color color, Sprite sprite = null, Vector2? pivot = null)
        {
            var rt = Ui.Rect(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = Ui.Background(rt, color);
            img.raycastTarget = false;
            if (sprite != null) img.sprite = sprite;
            return rt;
        }

        private Puppet MakePuppet(float anchorX, Color team, float dir)
        {
            var root = Ui.Rect(_stage, "Fighter");
            root.anchorMin = root.anchorMax = new Vector2(anchorX, 0.14f);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(220f, 330f);
            root.localScale = new Vector3(dir, 1f, 1f); // 오른쪽 장수는 좌우를 뒤집어 마주 본다
            var circle = UiShapes.Circle;
            var p = new Puppet { Root = root, Dir = dir, Team = team };
            Part(root, "Shadow", new Vector2(0f, 6f), new Vector2(170f, 34f), new Color(0f, 0f, 0f, 0.35f), circle);
            p.Aura = Part(root, "Aura", new Vector2(0f, 160f), new Vector2(300f, 300f), new Color(1f, 1f, 1f, 0f), circle);
            p.AuraImage = p.Aura.GetComponent<Image>();
            var leg = new Color(0.22f, 0.2f, 0.18f);
            Part(root, "LegL", new Vector2(-24f, 48f), new Vector2(26f, 92f), leg);
            Part(root, "LegR", new Vector2(24f, 48f), new Vector2(26f, 92f), leg);
            p.Body = Part(root, "Body", new Vector2(0f, 150f), new Vector2(100f, 120f), team).GetComponent<Image>();
            Part(root, "Belt", new Vector2(0f, 102f), new Vector2(104f, 14f), new Color(0.3f, 0.22f, 0.12f));
            // 무기: 일기토는 칼(긴 막대), 논쟁은 부채(짧은 판). 손 위치에서 돈다.
            p.WeaponRest = _debate ? -20f : -35f;
            p.Weapon = Part(root, "Weapon", new Vector2(56f, 150f), _debate ? new Vector2(46f, 70f) : new Vector2(14f, 170f),
                _debate ? new Color(0.92f, 0.86f, 0.7f) : new Color(0.75f, 0.77f, 0.8f), null, new Vector2(0.5f, 0f));
            p.Weapon.localRotation = Quaternion.Euler(0f, 0f, p.WeaponRest);
            Part(root, "Head", new Vector2(0f, 250f), new Vector2(76f, 76f), new Color(0.93f, 0.8f, 0.66f), circle);
            Part(root, "Helmet", new Vector2(0f, 284f), new Vector2(84f, 26f), Darker(team, 0.75f));
            // 말풍선 (뒤집히지 않게 무대에 따로)
            p.Bubble = Ui.Label(_stage, "", 32, TextAnchor.MiddleCenter);
            var bub = p.Bubble.rectTransform;
            bub.anchorMin = bub.anchorMax = new Vector2(anchorX, 0.14f);
            bub.pivot = new Vector2(0.5f, 0f);
            bub.sizeDelta = new Vector2(300f, 60f);
            bub.anchoredPosition = new Vector2(0f, 340f);
            return p;
        }

        private static Color Darker(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        private void ResetPuppet(Puppet p)
        {
            p.Root.anchoredPosition = Vector2.zero;
            p.Root.localRotation = Quaternion.identity;
            p.Weapon.localRotation = Quaternion.Euler(0f, 0f, p.WeaponRest);
            p.Body.color = p.Team;
            p.AuraImage.color = new Color(1f, 1f, 1f, 0f);
            p.Bubble.text = "";
        }

        // ---------------- 움직임 ----------------

        private void Animate(float delay, float duration, Action<float> step, Action done = null) =>
            _tweens.Add(new Tween { Start = _clock + delay, Duration = Mathf.Max(0.01f, duration), Step = step, Done = done });

        private void RunTweens(float dt)
        {
            _clock += dt;
            for (int i = 0; i < _tweens.Count; i++)
            {
                var tw = _tweens[i];
                float t = (_clock - tw.Start) / tw.Duration;
                if (t < 0f) continue;
                tw.Step(Mathf.Clamp01(t));
                if (t >= 1f) { tw.Done?.Invoke(); _tweens.RemoveAt(i); i--; }
            }
        }

        private void Move(Puppet p, float delay, float duration, float fromX, float toX, float hop = 0f) =>
            Animate(delay, duration, t => p.Root.anchoredPosition = new Vector2(p.Dir * Mathf.Lerp(fromX, toX, t), hop * Mathf.Sin(Mathf.PI * t)));

        private void Swing(Puppet p, float delay, float duration, float to) =>
            Animate(delay, duration, t => p.Weapon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(p.WeaponRest, to, Mathf.Sin(Mathf.PI * t))));

        private void Glow(Puppet p, float delay, float duration, Color color) =>
            Animate(delay, duration, t =>
            {
                var c = color;
                c.a = 0.65f * Mathf.Sin(Mathf.PI * t);
                p.AuraImage.color = c;
                p.Aura.localScale = Vector3.one * (0.7f + 0.5f * t);
            });

        /// <summary>고른 행동 (기본 행동 id: attack / evade / charge / bow / retreat / special).</summary>
        private void Act(Puppet p, Puppet other, string action)
        {
            switch (_play.Def.Action(action).Base)
            {
                case "attack":
                    Move(p, 0f, 0.25f, 0f, Dash);
                    Swing(p, 0.15f, 0.35f, _debate ? 40f : 95f);
                    Move(p, 0.6f, 0.3f, Dash, 0f);
                    break;
                case "evade":
                    Move(p, 0.1f, 0.35f, 0f, -90f, 50f);
                    Move(p, 0.9f, 0.3f, -90f, 0f);
                    break;
                case "charge":
                    Glow(p, 0f, 1.1f, new Color(1f, 0.85f, 0.25f));
                    break;
                case "bow":
                    Swing(p, 0f, 0.6f, 90f);
                    Arrow(p, other, 0.25f);
                    break;
                case "retreat":
                    Move(p, 0.1f, 0.5f, 0f, -140f);
                    break;
                case "special":
                    Glow(p, 0f, 0.5f, new Color(1f, 0.25f, 0.2f));
                    Move(p, 0.35f, 0.2f, 0f, Dash * 1.6f);
                    Swing(p, 0.4f, 0.35f, _debate ? 60f : 120f);
                    Move(p, 0.9f, 0.3f, Dash * 1.6f, 0f);
                    break;
            }
        }

        /// <summary>화살(논쟁은 날카로운 말): 손에서 상대 쪽으로 날아간다.</summary>
        private void Arrow(Puppet from, Puppet to, float delay)
        {
            var rt = Ui.Rect(_stage, "Arrow");
            rt.anchorMin = rt.anchorMax = from.Root.anchorMin;
            rt.sizeDelta = _debate ? new Vector2(60f, 40f) : new Vector2(70f, 8f);
            var img = Ui.Background(rt, _debate ? new Color(1f, 1f, 1f, 0f) : new Color(0.9f, 0.9f, 0.85f));
            img.raycastTarget = false;
            if (_debate) { var tx = Ui.Label(rt, "!!", 40, TextAnchor.MiddleCenter, Palette.Bad); Ui.Fill(tx.rectTransform); }
            float sx = 0f, ex = (to.Root.anchorMin.x - from.Root.anchorMin.x) * _stage.rect.width;
            rt.anchoredPosition = new Vector2(sx, 200f);
            rt.gameObject.SetActive(false);
            Animate(delay, 0.4f, t => { rt.gameObject.SetActive(true); rt.anchoredPosition = new Vector2(Mathf.Lerp(sx, ex, t), 200f + 30f * Mathf.Sin(Mathf.PI * t)); },
                () => UnityEngine.Object.Destroy(rt.gameObject));
        }

        /// <summary>맞음: 흔들림 + 붉은 번쩍임 + 피해 숫자 (회심이면 크게).</summary>
        private void Hit(Puppet p, float delay, long damage, bool crit)
        {
            Animate(delay, 0.35f, t =>
            {
                p.Root.anchoredPosition = new Vector2(p.Dir * -12f * Mathf.Sin(t * 30f) * (1f - t), 0f);
                p.Body.color = Color.Lerp(Color.white, p.Team, t);
            });
            Popup(p, delay, (crit ? T("ui.contest.crit") + " " : "") + "−" + damage, crit ? new Color(1f, 0.85f, 0.2f) : Palette.Bad, crit ? 60 : 44);
        }

        private void Popup(Puppet p, float delay, string text, Color color, int size)
        {
            var label = Ui.Label(_stage, text, size, TextAnchor.MiddleCenter, color);
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = p.Root.anchorMin;
            rt.sizeDelta = new Vector2(320f, 80f);
            rt.anchoredPosition = new Vector2(0f, 300f);
            label.gameObject.SetActive(false);
            Animate(delay, 1.0f, t =>
            {
                label.gameObject.SetActive(true);
                rt.anchoredPosition = new Vector2(0f, 300f + 90f * t);
                var c = color;
                c.a = 1f - t * t;
                label.color = c;
            }, () => UnityEngine.Object.Destroy(label.gameObject));
        }

        // ---------------- 진행 ----------------

        public override void Tick(float deltaTime)
        {
            if (_play == null) return;
            RunTweens(deltaTime);
            if (_phase == Phase.Over) return;
            _left -= deltaTime;
            if (_phase == Phase.Choosing)
            {
                if (_left <= 0f) { Reveal(); return; }
                _count.text = Mathf.CeilToInt(_left).ToString();
            }
            else if (_left <= 0f)
            {
                if (_play.IsOver) ShowOver(); else BeginChoosing();
            }
        }

        private void BeginChoosing()
        {
            _phase = Phase.Choosing;
            _left = ChooseSeconds;
            _pending = null;
            ResetPuppet(_a);
            ResetPuppet(_b);
            int number = _play.Rounds().Count + 1;
            _round.text = T("ui.contest.roundOf", number, _play.Def.MaxRounds);
            _count.text = Mathf.CeilToInt(_left).ToString();
            _banner.text = "";
            _effect.text = "";
            _detail.text = T(number == 1 ? "ui.contest.start" : "ui.contest.pick");
            BuildActions();
            RefreshGauges();
            Ui.SetEnabled(_autoButton, true);
            Ui.SetEnabled(_nextButton, false);
        }

        private void BuildActions()
        {
            Ui.Clear(_actions);
            _actionButtons.Clear();
            foreach (var id in _play.Available())
            {
                string a = id;
                bool special = _play.Def.Action(a).RequiresFullCharge;
                _actionButtons[a] = Ui.Button(_actions, ActionName(a), () => Pick(a), 100, special ? Palette.ButtonActive : (Color?)null, 34);
            }
        }

        private void Pick(string action)
        {
            if (_phase != Phase.Choosing) return;
            _pending = action;
            foreach (var kv in _actionButtons)
                kv.Value.targetGraphic.color = kv.Key == action ? Palette.Good : _play.Def.Action(kv.Key).RequiresFullCharge ? Palette.ButtonActive : Palette.Button;
            _detail.text = T("ui.contest.chosen", ActionName(action));
        }

        /// <summary>동시 공개: 고른 것(없으면 무작위)으로 라운드를 돌리고 두 장수가 행동한다. 그다음 맞음·기운 변화.</summary>
        private void Reveal()
        {
            var before = _play.Rounds();
            bool timeout = _pending == null;
            if (timeout || !_play.Choose(_pending)) _play.ChooseRandom(_plan.Setup.Seed);
            var rounds = _play.Rounds();
            _phase = Phase.Reveal;
            _left = RevealSeconds;
            _count.text = "";
            foreach (var b in _actionButtons.Values) Ui.SetEnabled(b, false);
            if (rounds.Count == before.Count) { _left = 0f; return; }
            var r = rounds[rounds.Count - 1];
            var prev = before.Count > 0 ? before[before.Count - 1] : null;

            _a.Bubble.text = Ui.Colored(ActionName(r.ActionA), Palette.Ally);
            _b.Bubble.text = Ui.Colored(ActionName(r.ActionB), Palette.Enemy);
            Animate(1.6f, 0.01f, _ => { _a.Bubble.text = ""; _b.Bubble.text = ""; });
            Act(_a, _b, r.ActionA);
            Act(_b, _a, r.ActionB);

            // 결과: 맞은 쪽 흔들림·숫자, 기운 변화
            var def = _play.Def;
            long dmgA = ((prev?.ResourceA ?? def.Start) - r.ResourceA).FloorToLong();
            long dmgB = ((prev?.ResourceB ?? def.Start) - r.ResourceB).FloorToLong();
            if (dmgA > 0) Hit(_a, 0.55f, dmgA, r.CritOnA);
            if (dmgB > 0) Hit(_b, 0.55f, dmgB, r.CritOnB);
            long gainA = (r.ChargeA - (prev?.ChargeA ?? Fix.Zero)).FloorToLong();
            long gainB = (r.ChargeB - (prev?.ChargeB ?? Fix.Zero)).FloorToLong();
            if (gainA > 0) Popup(_a, 0.9f, T(def.ChargeKey) + " +" + gainA + "%", new Color(1f, 0.85f, 0.3f), 34);
            if (gainB > 0) Popup(_b, 0.9f, T(def.ChargeKey) + " +" + gainB + "%", new Color(1f, 0.85f, 0.3f), 34);
            Animate(0.6f, 0.01f, _ => RefreshGauges());

            _detail.text = (timeout ? Ui.Colored(T("ui.contest.timeout"), Palette.TextDim) + "\n" : "") + (dmgA <= 0 && dmgB <= 0 && gainA <= 0 && gainB <= 0 ? T("ui.contest.nothing") : "");
        }

        private void ShowOver()
        {
            _phase = Phase.Over;
            Ui.Clear(_actions);
            _actionButtons.Clear();
            RefreshGauges();
            _round.text = "";
            _count.text = "";
            var r = _play.Result;
            _banner.text = ResultText(r);
            _effect.text = EffectText(r);
            // 쓰러진 쪽은 넘어지고, 물러난 쪽은 무대 밖으로
            if (r.End == ContestEnds.Fall && r.Winner != null)
            {
                var loser = r.Winner == "A" ? _b : _a;
                Animate(0.1f, 0.6f, t => { loser.Root.localRotation = Quaternion.Euler(0f, 0f, 85f * t); loser.Body.color = Color.Lerp(loser.Team, Darker(loser.Team, 0.5f), t); });
            }
            if (r.End == ContestEnds.Retreat && r.Retreater != null)
            {
                var gone = r.Retreater == "A" ? _a : _b;
                Move(gone, 0.1f, 0.9f, 0f, -700f);
            }
            _detail.text = "";
            Ui.SetEnabled(_autoButton, false);
            Ui.SetEnabled(_nextButton, true);
        }

        private void Next()
        {
            if (!_play.IsOver) return;
            if (_index + 1 < _plan.Setup.Contests.Count) App.Show(new ContestScreen(_plan, _index + 1));
            else App.Show(new BattleScreen(_plan));
        }

        private string ResultText(ContestResult r)
        {
            if (r.End == ContestEnds.Retreat) return Ui.Colored(T(r.Retreater == "A" ? "ui.contest.iRetreated" : "ui.contest.theyRetreated"), Palette.TextDim);
            if (r.Winner == null) return T("ui.contest.draw");
            bool won = r.Winner == "A";
            string key = r.End == ContestEnds.Fall ? (won ? "ui.contest.wonFall" : "ui.contest.lostFall") : (won ? "ui.contest.won" : "ui.contest.lost");
            return Ui.Colored(T(key), won ? Palette.Good : Palette.Bad);
        }

        /// <summary>전투에 주는 영향 (battle.md 2.7.5 일기토, 2.7.6 논쟁).</summary>
        private string EffectText(ContestResult r)
        {
            string outcome;
            if (r.End == ContestEnds.Retreat) outcome = r.Retreater == "A" ? "iRetreated" : "theyRetreated";
            else if (r.Winner == null) outcome = "draw";
            else outcome = (r.Winner == "A" ? "won" : "lost") + (r.End == ContestEnds.Fall ? "Fall" : "");
            string key = "ui.contest.effect." + _play.Def.Id + "." + outcome;
            return Ctx.Defs.Strings.ContainsKey(key) ? T(key) : "";
        }

        private string ActionName(string id) => T(_play.Def.Action(id).NameKey);

        private string GeneralOf(string corpsId)
        {
            foreach (var c in _plan.Setup.Corps) if (c.Id == corpsId) return c.General?.Id;
            return null;
        }
    }
}
