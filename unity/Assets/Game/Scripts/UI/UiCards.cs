using System;
using System.Collections.Generic;
using Game.Campaign.Roster;
using Game.Unity.Boot;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Unity.UI
{
    /// <summary>
    /// 카드 체계 (docs/design/ui-kit.md 4.4): 카드 = 내용 (장수 · 병종 · 군단 · 아이템) × 밀도 (미니 · 간편 · 보통 · 세밀) × 틀 (스타일 데이터 card.&lt;내용&gt;.&lt;밀도&gt;).
    /// 화면은 Cards.General / Troop / Corps / Item 으로만 부르고, 겉모양(띠 · 등급 테두리 · 장식 테두리 · 그림)은 UiKit.DressCard가 입힌다.
    /// 전투 부대 고르기 같은 새 화면도 같은 내용 · 밀도를 그대로 쓴다.
    /// </summary>
    public static class Cards
    {
        public enum Density { Mini, Compact, Standard, Detail }

        public static string Key(Density d) => d == Density.Mini ? "mini" : d == Density.Compact ? "compact" : d == Density.Standard ? "standard" : "detail";
        public static string Role(string content, Density d) => "card." + content + "." + Key(d);

        // ---------------- 내용 ----------------

        public sealed class TroopInfo
        {
            public string UnitId, TroopType, Name, TierName;
            public int Tier;
            public long Troops, Cap, Wounded;
            public int TrainingPct;
            /// <summary>결속 (0 ~ CohesionMax, 퍼센트 아님 — 병력 목록과 같은 수).</summary>
            public long Cohesion, CohesionMax = 1;
            public long Attack, Defense, Speed;
        }

        public sealed class CorpsInfo
        {
            public string GeneralId, GeneralName, FormationName, RoleName;
            public int Level;
            public long Total, Cap;
            public bool Commander;
            public readonly List<(string type, string name, long count)> Troops = new List<(string, string, long)>();
        }

        public sealed class ItemInfo
        {
            public string TroopType, Name, Sub, Price, State;
            public Color? GradeColor;
        }

        public static Ui.GeneralCardData InfoOf(GameContext ctx, GeneralState g)
        {
            ctx.Defs.Generals.TryGetValue(g.GeneralId, out var def);
            string grade = def?.Grade;
            long Stat(string id) => CorpsValidator.EffectiveStat(ctx.Defs, g, id).FloorToLong();
            return new Ui.GeneralCardData
            {
                Id = g.GeneralId,
                Name = ctx.GeneralName(g.GeneralId),
                Grade = grade,
                GradeName = string.IsNullOrEmpty(grade) ? null : ctx.T("grade." + grade),
                Level = g.Level,
                StatusText = g.Status == GeneralStatus.Available ? null : ctx.T("ui.generals." + g.Status),
                StatusColor = g.Status == GeneralStatus.LightInjury ? new Color(0.851f, 0.651f, 0.18f) : g.Status == GeneralStatus.SeriousInjury ? new Color(0.851f, 0.463f, 0.18f) : new Color(0.69f, 0.227f, 0.18f),
                Leadership = Stat("leadership"),
                Might = Stat("might"),
                Intellect = Stat("intellect"),
                Attention = Game.Flow.GeneralRoster.NeedsAttention(ctx.Defs, ctx.State, g.GeneralId),
            };
        }

        public static TroopInfo InfoOf(GameContext ctx, UnitState u, long cap = 0)
        {
            ctx.Defs.TroopTypes.TryGetValue(u.TroopType ?? "", out var t);
            return new TroopInfo
            {
                UnitId = u.UnitId,
                TroopType = u.TroopType,
                Name = t != null ? ctx.T(t.NameKey) : u.TroopType,
                Tier = t?.Tier ?? 1,
                Troops = u.Troops,
                Cap = cap > 0 ? cap : u.Troops,
                Wounded = u.Wounded,
                TrainingPct = (int)(u.Training * 100).FloorToLong(),
                Cohesion = u.Cohesion.FloorToLong(),
                CohesionMax = System.Math.Max(1, ctx.Defs.Campaign.CohesionMax.FloorToLong()),
                Attack = t?.Stats?.Get("attack").FloorToLong() ?? 0,
                Defense = t?.Stats?.Get("defense").FloorToLong() ?? 0,
                Speed = t?.Stats?.Get("speed").FloorToLong() ?? 0,
            };
        }

        public static CorpsInfo InfoOf(GameContext ctx, CorpsComposition c, long cap = 0)
        {
            var info = new CorpsInfo { GeneralId = c.GeneralId, GeneralName = c.GeneralId == null ? "-" : ctx.GeneralName(c.GeneralId), Commander = c.Commander, Cap = cap };
            var g = c.GeneralId == null ? null : ctx.State.Generals.Find(x => x.GeneralId == c.GeneralId);
            info.Level = g?.Level ?? 0;
            info.FormationName = c.Formation != null && ctx.Defs.Formations.TryGetValue(c.Formation, out var f) ? ctx.T(f.NameKey) : c.Formation;
            info.RoleName = c.Role == null ? null : ctx.T("role." + c.Role);
            foreach (var p in c.Picks)
            {
                var u = ctx.State.FindUnit(p.UnitId);
                string type = u?.TroopType ?? p.UnitId;
                string name = ctx.Defs.TroopTypes.TryGetValue(type ?? "", out var t) ? ctx.T(t.NameKey) : type;
                info.Troops.Add((type, name, p.Troops));
                info.Total += p.Troops;
            }
            if (info.Cap <= 0) info.Cap = info.Total;
            return info;
        }

        // ---------------- 장수 ----------------

        public static RectTransform General(GameContext ctx, Transform parent, Ui.GeneralCardData d, Density density, Action onOpen = null, Action<RectTransform> onInfo = null)
        {
            string role = Role("general", density);
            var grade = GradeLabel.ColorOf(d.Grade);
            switch (density)
            {
                case Density.Mini:
                    {
                        var b = Ui.MiniCard(ctx, parent, null, d.Id, d.Name, "Lv." + d.Level, d.StatusText != null ? Ui.MiniState.Dim : Ui.MiniState.Normal, onOpen, null, d.StatusText, d.StatusColor, 150, 178);
                        return (RectTransform)b.transform;
                    }
                case Density.Standard:
                    {
                        var card = Ui.GeneralCard(ctx, parent, d, onOpen, onInfo);
                        return card;
                    }
                case Density.Compact:
                    {
                        var (card, bg) = Shell(parent, role, 140, onOpen);
                        Portrait(card, d.Id, d.Name, 104, 18);
                        var name = Text(card, d.Name + "  Lv." + d.Level, 30, 140, 16, -20, 44, FontStyle.Bold);
                        if (d.GradeName != null) Pill(card, d.GradeName, grade, -18, 18);
                        Text(card, StatLine(ctx, d), 24, 140, 70, -20, 40, FontStyle.Normal, Palette.TextDim);
                        if (d.StatusText != null) Pill(card, d.StatusText, d.StatusColor, -18, 80);
                        UiKit.DressCard(card, bg, role, grade);
                        return card;
                    }
                default:
                    {
                        var (card, bg) = Shell(parent, role, 420, onOpen);
                        var pic = Box(card, "PicBack", 18, 18, 240, 240);
                        var pb = pic.gameObject.AddComponent<Image>();
                        pb.color = Palette.PictureBack; pb.sprite = UiShapes.Rounded(14); pb.type = Image.Type.Sliced; pb.raycastTarget = false;
                        UiKit.PictureWell(pb);
                        Portrait(pic, d.Id, d.Name, 190, 25);
                        Text(card, d.Name, 44, 280, 22, -20, 56, FontStyle.Bold);
                        Text(card, "Lv." + d.Level + (d.GradeName != null ? "  ·  " + d.GradeName : ""), 28, 280, 84, -20, 40, FontStyle.Normal, grade);
                        if (d.StatusText != null) Pill(card, d.StatusText, d.StatusColor, -18, 24);
                        float y = 140;
                        foreach (var (id, val) in new[] { ("leadership", d.Leadership), ("might", d.Might), ("intellect", d.Intellect) })
                        {
                            Text(card, ctx.T("stat." + id), 26, 280, y, -380, 40, FontStyle.Normal, Palette.TextDim);
                            Bar(card, 420, y + 12, -110, 16, Mathf.Clamp01(val / 120f), Palette.ButtonActive);
                            Text(card, val.ToString(), 28, -96, y, -20, 40, FontStyle.Bold, null, TextAnchor.MiddleRight, true);
                            y += 50;
                        }
                        var g = ctx.State.Generals.Find(x => x.GeneralId == d.Id);
                        var lines = new List<string>();
                        if (g != null && g.LearnedTraits.Count > 0) lines.Add(ctx.T("ui.card.traits") + " " + string.Join(" · ", g.LearnedTraits.ConvertAll(t => ctx.T("trait." + t))));
                        if (g != null && g.LearnedSkills.Count > 0) lines.Add(ctx.T("ui.card.skills") + " " + string.Join(" · ", g.LearnedSkills.ConvertAll(s => ctx.T("skill." + s))));
                        Text(card, lines.Count > 0 ? string.Join("\n", lines) : ctx.T("ui.card.none"), 24, 18, 300, -20, 100, FontStyle.Normal, Palette.TextDim);
                        UiKit.DressCard(card, bg, role, grade);
                        return card;
                    }
            }
        }

        private static string StatLine(GameContext ctx, Ui.GeneralCardData d) =>
            ctx.T("stat.leadership") + " " + d.Leadership + "   " + ctx.T("stat.might") + " " + d.Might + "   " + ctx.T("stat.intellect") + " " + d.Intellect;

        // ---------------- 병종 ----------------

        public static RectTransform Troop(GameContext ctx, Transform parent, TroopInfo t, Density density, Action onOpen = null)
        {
            string role = Role("troop", density);
            switch (density)
            {
                case Density.Mini:
                    return (RectTransform)Ui.MiniCard(ctx, parent, t.TroopType, null, t.Name, t.Troops.ToString("N0"), t.Troops > 0 ? Ui.MiniState.Normal : Ui.MiniState.Dim, onOpen).transform;
                case Density.Compact:
                    {
                        var (card, bg) = Shell(parent, role, 130, onOpen);
                        Picture(ctx, card, t.TroopType, 104, 14);
                        Text(card, t.Name, 30, 136, 14, -200, 44, FontStyle.Bold);
                        Text(card, ctx.T("ui.card.trainShort", t.TrainingPct) + "   " + ctx.T("ui.card.cohesionShort", t.Cohesion), 22, 136, 62, -200, 40, FontStyle.Normal, Palette.TextDim);
                        Text(card, t.Troops.ToString("N0"), 34, -190, 14, -20, 50, FontStyle.Bold, null, TextAnchor.MiddleRight, true);
                        if (t.Wounded > 0) Text(card, ctx.T("ui.card.wounded", t.Wounded.ToString("N0")), 20, -190, 66, -20, 36, FontStyle.Normal, Palette.Bad, TextAnchor.MiddleRight, true);
                        UiKit.DressCard(card, bg, role, null);
                        return card;
                    }
                case Density.Standard:
                    {
                        var (card, bg) = Shell(parent, role, 230, onOpen);
                        Picture(ctx, card, t.TroopType, 190, 20);
                        Text(card, t.Name, 36, 232, 16, -20, 50, FontStyle.Bold);
                        float y = 74;
                        GaugeLine(ctx.T("ui.card.troops"), t.Cap > 0 ? (float)t.Troops / t.Cap : 1f, new Color(0.247f, 0.435f, 0.69f), t.Troops.ToString("N0"));
                        GaugeLine(ctx.T("ui.card.training"), t.TrainingPct / 100f, Palette.Good, t.TrainingPct + "%");
                        GaugeLine(ctx.T("ui.card.cohesion"), (float)t.Cohesion / t.CohesionMax, new Color(0.85f, 0.65f, 0.2f), t.Cohesion.ToString());
                        void GaugeLine(string label, float r, Color c, string value)
                        {
                            Text(card, label, 24, 232, y, -560, 40, FontStyle.Normal, Palette.TextDim);
                            Bar(card, 340, y + 12, -170, 16, Mathf.Clamp01(r), c);
                            Text(card, value, 26, -160, y, -20, 40, FontStyle.Bold, null, TextAnchor.MiddleRight, true);
                            y += 48;
                        }
                        UiKit.DressCard(card, bg, role, null);
                        return card;
                    }
                default:
                    {
                        var (card, bg) = Shell(parent, role, 380, onOpen);
                        Picture(ctx, card, t.TroopType, 240, 20);
                        Text(card, t.Name, 44, 280, 20, -20, 56, FontStyle.Bold);
                        Text(card, ctx.T("ui.card.tier", t.Tier) + "   " + t.Troops.ToString("N0") + (t.Wounded > 0 ? "  (" + ctx.T("ui.card.wounded", t.Wounded.ToString("N0")) + ")" : ""), 26, 280, 80, -20, 40, FontStyle.Normal, Palette.TextDim);
                        float y = 130;
                        foreach (var (key, val, max) in new[] { ("ui.card.attack", t.Attack, 100L), ("ui.card.defense", t.Defense, 100L), ("ui.card.speed", t.Speed, 100L) })
                        {
                            Text(card, ctx.T(key), 24, 280, y, -560, 40, FontStyle.Normal, Palette.TextDim);
                            Bar(card, 400, y + 12, -110, 16, Mathf.Clamp01((float)val / max), Palette.ButtonActive);
                            Text(card, val.ToString(), 26, -96, y, -20, 40, FontStyle.Bold, null, TextAnchor.MiddleRight, true);
                            y += 46;
                        }
                        Text(card, ctx.T("ui.card.trainShort", t.TrainingPct) + "   " + ctx.T("ui.card.cohesionShort", t.Cohesion), 24, 20, 290, -20, 40, FontStyle.Normal, Palette.TextDim);
                        UiKit.DressCard(card, bg, role, null);
                        return card;
                    }
            }
        }

        // ---------------- 군단 ----------------

        public static RectTransform Corps(GameContext ctx, Transform parent, CorpsInfo c, Density density, Action onOpen = null)
        {
            string role = Role("corps", density);
            switch (density)
            {
                case Density.Mini:
                    {
                        var (card, bg) = Shell(parent, role, 178, onOpen, 150);
                        Portrait(card, c.GeneralId, c.GeneralName, 96, 27, 10);
                        Text(card, (c.Commander ? "★ " : "") + c.GeneralName, 22, 4, 112, -4, 30, FontStyle.Bold, null, TextAnchor.MiddleCenter);
                        Text(card, c.Total.ToString("N0"), 20, 4, 142, -4, 28, FontStyle.Normal, Palette.TextDim, TextAnchor.MiddleCenter);
                        UiKit.DressCard(card, bg, role, null);
                        return card;
                    }
                case Density.Compact:
                    {
                        var (card, bg) = Shell(parent, role, 140, onOpen);
                        Portrait(card, c.GeneralId, c.GeneralName, 104, 18);
                        Text(card, (c.Commander ? "★ " : "") + c.GeneralName + (c.Level > 0 ? "  Lv." + c.Level : ""), 28, 140, 14, -220, 44, FontStyle.Bold);
                        Text(card, c.FormationName ?? "", 22, 140, 60, -220, 36, FontStyle.Normal, Palette.TextDim);
                        float x = 140;
                        foreach (var tr in c.Troops)
                        {
                            if (x > 560) break;
                            var pic = Box(card, "T", x, 96, 34, 34);
                            Ui.Fill(Ui.TroopPicture(ctx, pic, tr.type, 34));
                            Text(card, tr.count.ToString("N0"), 18, x + 36, 96, -1, 34, FontStyle.Normal, Palette.TextDim, TextAnchor.MiddleLeft, false, 90);
                            x += 130;
                        }
                        Text(card, c.Total.ToString("N0"), 32, -210, 14, -20, 50, FontStyle.Bold, null, TextAnchor.MiddleRight, true);
                        UiKit.DressCard(card, bg, role, null);
                        return card;
                    }
                case Density.Standard:
                case Density.Detail:
                    {
                        bool detail = density == Density.Detail;
                        float rowsH = Mathf.Max(1, c.Troops.Count) * 56;
                        var (card, bg) = Shell(parent, role, 150 + rowsH + (detail ? 60 : 0), onOpen);
                        Portrait(card, c.GeneralId, c.GeneralName, 110, 18);
                        Text(card, (c.Commander ? "★ " : "") + c.GeneralName + (c.Level > 0 ? "  Lv." + c.Level : ""), 32, 146, 18, -230, 48, FontStyle.Bold);
                        Text(card, (c.FormationName ?? "") + (c.RoleName != null ? "  ·  " + c.RoleName : ""), 24, 146, 70, -230, 40, FontStyle.Normal, Palette.TextDim);
                        Text(card, c.Total.ToString("N0") + (c.Cap > 0 && c.Cap != c.Total ? " / " + c.Cap.ToString("N0") : ""), 30, -260, 18, -20, 48, FontStyle.Bold, null, TextAnchor.MiddleRight, true);
                        float y = 140;
                        foreach (var tr in c.Troops)
                        {
                            var pic = Box(card, "T", 24, y, 48, 48);
                            Ui.Fill(Ui.TroopPicture(ctx, pic, tr.type, 48));
                            Text(card, tr.name, 24, 84, y + 4, -560, 40);
                            Bar(card, 260, y + 16, -170, 16, c.Total > 0 ? (float)tr.count / c.Total : 0f, new Color(0.247f, 0.435f, 0.69f));
                            Text(card, tr.count.ToString("N0"), 24, -160, y + 4, -20, 40, FontStyle.Bold, null, TextAnchor.MiddleRight, true);
                            y += 56;
                        }
                        if (detail)
                        {
                            long supply = 0;
                            foreach (var tr in c.Troops) if (ctx.Defs.TroopTypes.TryGetValue(tr.type ?? "", out var td)) supply += td.SupplyPerDay * tr.count;
                            Text(card, ctx.T("ui.card.supply", supply.ToString("N0")), 24, 24, y + 6, -20, 40, FontStyle.Normal, Palette.TextDim);
                        }
                        UiKit.DressCard(card, bg, role, null);
                        return card;
                    }
            }
            return null;
        }

        // ---------------- 아이템 ----------------

        public static RectTransform Item(GameContext ctx, Transform parent, ItemInfo it, Density density, Action onOpen = null)
        {
            string role = Role("item", density);
            switch (density)
            {
                case Density.Mini:
                    return (RectTransform)Ui.MiniCard(ctx, parent, it.TroopType, null, it.Name, it.Price ?? it.State ?? "", Ui.MiniState.Normal, onOpen, null, null, default, 150, 178).transform;
                case Density.Compact:
                    {
                        var (card, bg) = Shell(parent, role, 120, onOpen);
                        Picture(ctx, card, it.TroopType, 96, 12);
                        Text(card, it.Name, 28, 126, 12, -220, 44, FontStyle.Bold);
                        Text(card, it.Sub ?? "", 22, 126, 58, -220, 40, FontStyle.Normal, Palette.TextDim);
                        Text(card, it.Price ?? it.State ?? "", 28, -220, 12, -20, 96, FontStyle.Bold, null, TextAnchor.MiddleRight, true);
                        UiKit.DressCard(card, bg, role, it.GradeColor);
                        return card;
                    }
                default:
                    {
                        bool detail = density == Density.Detail;
                        var (card, bg) = Shell(parent, role, detail ? 560 : 380, onOpen, detail ? -1 : 320);
                        var pic = Box(card, "PicBack", 14, 14, -14, detail ? 400 : 240, true);
                        var pb = pic.gameObject.AddComponent<Image>();
                        pb.color = Palette.PictureBack; pb.sprite = UiShapes.Rounded(12); pb.type = Image.Type.Sliced; pb.raycastTarget = false;
                        UiKit.PictureWell(pb);
                        var inner = Box(pic, "Pic", 0, 0, 0, 0, true, true);
                        var tp = Ui.TroopPicture(ctx, inner, it.TroopType, detail ? 380 : 220);
                        tp.anchorMin = tp.anchorMax = new Vector2(0.5f, 0.5f);
                        tp.sizeDelta = new Vector2(detail ? 380 : 220, detail ? 380 : 220);
                        float y = detail ? 424 : 262;
                        Text(card, it.Name, 28, 10, y, -10, 40, FontStyle.Bold, null, TextAnchor.MiddleCenter);
                        Text(card, (it.Sub ?? "") + (it.Price != null ? "  ·  " + it.Price : ""), 22, 10, y + 42, -10, 34, FontStyle.Normal, Palette.TextDim, TextAnchor.MiddleCenter);
                        if (it.State != null) Pill(card, it.State, Palette.ButtonActive, -14, 20);
                        UiKit.DressCard(card, bg, role, it.GradeColor);
                        return card;
                    }
            }
        }

        // ---------------- 그리기 도우미 ----------------

        /// <summary>카드 판: 높이 고정, 너비는 부모 따라 (width &gt; 0이면 고정). 바탕 Image와 누르기.</summary>
        private static (RectTransform card, Image bg) Shell(Transform parent, string role, float height, Action onOpen, float width = -1)
        {
            var card = Ui.Rect(parent, role);
            var le = Ui.Size(card, height, width);
            if (width > 0) le.flexibleWidth = 0;
            var bg = Ui.Background(card, Palette.Card);
            Ui.Round(bg);
            if (onOpen != null)
            {
                var b = card.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.onClick.AddListener(() => { Sfx.Play("click", 0.5f); onOpen(); });
            }
            return (card, bg);
        }

        /// <summary>
        /// 위 · 왼쪽 기준 자리: x · y = 왼쪽 위에서. right ≤ 0 이면 오른쪽 끝에서 그만큼 안쪽 (너비 대신), 양수면 너비.
        /// x가 음수면 오른쪽 끝에서 |x|.
        /// </summary>
        private static RectTransform Box(Transform parent, string name, float x, float y, float right, float h, bool stretch = false, bool fill = false)
        {
            var rt = Ui.Rect(parent, name);
            if (fill) { Ui.Fill(rt); return rt; }
            rt.pivot = new Vector2(0, 1);
            bool fromRight = x < 0;
            bool rightEdge = right <= 0 || stretch;
            rt.anchorMin = new Vector2(fromRight ? 1 : 0, 1);
            rt.anchorMax = new Vector2(fromRight || rightEdge ? 1 : 0, 1);
            if (rightEdge && !fromRight)
            {
                rt.offsetMin = new Vector2(x, -y - h);
                rt.offsetMax = new Vector2(right, -y);
            }
            else if (fromRight)
            {
                rt.offsetMin = new Vector2(x, -y - h);
                rt.offsetMax = new Vector2(right <= 0 ? right : x + right, -y);
            }
            else
            {
                rt.sizeDelta = new Vector2(right, h);
                rt.anchoredPosition = new Vector2(x, -y);
            }
            return rt;
        }

        private static Text Text(Transform parent, string s, int size, float x, float y, float right, float h, FontStyle style = FontStyle.Normal, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft, bool fromRight = false, float width = -1)
        {
            var rt = width > 0 ? Box(parent, "Text", x, y, width, h) : Box(parent, "Text", x, y, right, h);
            var t = Ui.Label(rt, s, Mathf.RoundToInt(size * UiStyle.FontScale), align, color ?? Palette.Text);
            Ui.Fill(t.rectTransform);
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        private static void Bar(Transform parent, float x, float y, float right, float h, float ratio, Color fill)
        {
            var track = Box(parent, "Track", x, y, right, h);
            var ti = track.gameObject.AddComponent<Image>();
            ti.color = Palette.Track; ti.sprite = UiShapes.Rounded(Mathf.RoundToInt(h / 2)); ti.type = Image.Type.Sliced; ti.raycastTarget = false;
            var f = Ui.Rect(track, "Fill");
            f.anchorMin = Vector2.zero;
            f.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
            f.offsetMin = f.offsetMax = Vector2.zero;
            var fi = f.gameObject.AddComponent<Image>();
            fi.color = fill; fi.sprite = ti.sprite; fi.type = Image.Type.Sliced; fi.raycastTarget = false;
            f.gameObject.SetActive(ratio > 0.001f);
        }

        private static void Pill(Transform parent, string s, Color color, float x, float y)
        {
            float w = Mathf.Max(70, s.Length * 24 + 28);
            var rt = Box(parent, "Pill", x - w, y, w, 40);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color; img.sprite = UiShapes.Rounded(20); img.type = Image.Type.Sliced; img.raycastTarget = false;
            var t = Ui.Label(rt, s, 22, TextAnchor.MiddleCenter, Palette.TextOn(color));
            Ui.Fill(t.rectTransform);
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
        }

        private static void Portrait(Transform parent, string generalId, string name, float size, float x, float y = -1)
        {
            var holder = Box(parent, "Portrait", x, y >= 0 ? y : 18, size, size);
            var p = new Portrait(holder, size);
            p.Rect.anchorMin = p.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            p.Rect.sizeDelta = new Vector2(size, size);
            p.Show(generalId, name, Palette.Ally);
            foreach (var g in holder.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
        }

        private static void Picture(GameContext ctx, Transform parent, string troopType, float size, float x)
        {
            var holder = Box(parent, "Picture", x, 12, size, size);
            Ui.Fill(Ui.TroopPicture(ctx, holder, troopType, size));
        }
    }
}
