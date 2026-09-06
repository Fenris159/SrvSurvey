using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Groups separated segments of the selected HUD color and assigns them to calibrated rig slots.</summary>
internal static class MiningColorBarDetector
{
    internal static MiningBarAnalysis Analyze(IFssPixelSource source, MiningDetectionSettings settings,
        MiningBarAnalysis? previous = null)
    {
        var offsetX = previous?.OffsetX ?? 0;
        var offsetY = previous?.OffsetY ?? 0;
        var image = new MiningHudImage(source, settings.CircleWidth);
        var mask = new ColorMask(image, settings.BarColor);
        var radius = image.Radius;
        var geometry = new MiningHudGeometry(settings);
        var states = new MiningBarState[6];
        var scores = new double[6];
        var centers = settings.Markers.Select(p => (X: p.X * image.Width, Y: p.Y * image.Height)).ToArray();
        var candidates = new List<(double X, double Y, double Score)>();
        var fragments = new List<Group>();
        foreach (var group in FindGroups(mask))
        {
            var width = group.MaxX - group.MinX + 1;
            var height = group.MaxY - group.MinY + 1;
            if (group.Points.Count >= 6 && width >= radius * .25 && width <= radius * 2.9 && height <= radius * 1.25)
                fragments.Add(group);
            if (group.Points.Count < 12 || width < radius * 1.2 || width > radius * 2.9
                || height < 2 || height > radius * 1.25) continue;
            var measuredRadius = Math.Clamp(width / 2.15, radius * .8, radius * 1.2);
            var offset = geometry.Transform(MiningBarShape.Centroid.X, MiningBarShape.Centroid.Y, measuredRadius);
            var x = group.Points.Average(p => p.X) - offset.X;
            var y = group.Points.Average(p => p.Y) - offset.Y - radius * settings.BarGap;
            var score = ScoreGroup(mask, x, y, measuredRadius, geometry, settings.BarGap);
            if (score >= .7) candidates.Add((x, y, score));
        }
        var allowance = settings.GetMovementAllowance(image.Width) + 4;
        // Position comes from the HUD layout, not which bar happened to survive the last frame.
        var locatedGrid = previous?.HasAnchor == true
            ? MiningCircleMask.LocateGrid(image, centers, radius, geometry, offsetX, offsetY, allowance) : null;
        if (locatedGrid is { } located) (offsetX, offsetY) = located;
        var bestCount = 0;
        var bestDistance = double.MaxValue;
        var assignments = new Dictionary<int, int>();
        foreach (var candidate in candidates)
            for (var anchor = 0; anchor < 6; anchor++)
            {
                // Rig 1 is deployed first. Once established, retain slot identities even
                // when only a later rig remains visible.
                if (previous?.HasAnchor != true && candidates.Count == 1 && anchor != 0) continue;
                var dx = candidate.X - centers[anchor].X;
                var dy = candidate.Y - centers[anchor].Y;
                var distance = Math.Pow(dx - offsetX, 2) + Math.Pow(dy - offsetY, 2);
                var matches = new Dictionary<int, int>();
                for (var i = 0; i < candidates.Count; i++)
                {
                    var point = candidates[i];
                    var nearest = centers.Select((p, slot) => (Slot: slot,
                        Distance: Math.Pow(point.X - p.X - dx, 2) + Math.Pow(point.Y - p.Y - dy, 2)))
                        .OrderBy(p => p.Distance).First();
                    if (nearest.Distance > Math.Pow(radius * .6, 2)) continue;
                    if (!matches.TryAdd(nearest.Slot, i)) { matches.Clear(); break; }
                }
                if (matches.Count != candidates.Count) continue;
                var previousSlots = previous?.AnchorSlots ?? 0;
                var matchedSlots = matches.Keys.Aggregate(0, (value, slot) => value | (1 << slot));
                var lostSlots = previousSlots & ~matchedSlots;
                var replacedSlots = lostSlots != 0 && (matchedSlots & ~previousSlots) != 0;
                var retained = (previousSlots & matchedSlots) != 0;
                var groupConfirmsMovement = matches.Count >= 2 && lostSlots == 0;
                // Never slide the grid just to keep a previously visible rig number.
                // A large move or disjoint identity needs independent layout evidence.
                if (previous?.HasAnchor == true && (distance > radius * radius && !groupConfirmsMovement || !retained || replacedSlots)
                    && !(locatedGrid is not null && distance <= radius * radius))
                {
                    var verified = MiningCircleMask.LocateGrid(image, centers, radius, geometry,
                        dx, dy, allowance, retained && !replacedSlots ? 4 : 6);
                    if (verified is not { } pose || Math.Pow(pose.X - dx, 2) + Math.Pow(pose.Y - dy, 2) > radius * radius * .36)
                        continue;
                }
                if (matches.Count < bestCount || matches.Count == bestCount && distance >= bestDistance) continue;
                bestCount = matches.Count; bestDistance = distance;
                assignments = matches;
            }
        if (assignments.Count > 0)
        {
            var nextX = assignments.Select(p => candidates[p.Value].X - centers[p.Key].X).Average();
            var nextY = assignments.Select(p => candidates[p.Value].Y - centers[p.Key].Y).Average();
            if (locatedGrid is { } position
                && Math.Pow(nextX - position.X, 2) + Math.Pow(nextY - position.Y, 2) <= radius * radius * .36)
                (nextX, nextY) = position;
            // Reject an out-of-range identity, rather than trying a different row inside the margin.
            if (Math.Abs(nextX) > allowance || Math.Abs(nextY) > allowance) assignments.Clear();
            else if (assignments.Count > 0) { offsetX = nextX; offsetY = nextY; }
        }
        if (assignments.Count == 0 && candidates.Count > 0)
        {
            return new(states, offsetX, offsetY)
            {
                HasAnchor = previous?.HasAnchor == true,
                AnchorSlots = previous?.AnchorSlots ?? 0
            };
        }
        if (candidates.Count == 0)
        {
            // With the last bar gone, reacquire the empty grid rather than checking stale centers.
            var grid = locatedGrid ?? MiningCircleMask.LocateGrid(image, centers, radius, geometry, offsetX, offsetY, allowance);
            if (grid is null)
                return new(states, offsetX, offsetY)
                {
                    HasAnchor = previous?.HasAnchor == true,
                    AnchorSlots = previous?.AnchorSlots ?? 0
                };
            (offsetX, offsetY) = grid.Value;
        }
        for (var i = 0; i < 6; i++)
        {
            if (assignments.TryGetValue(i, out var candidate))
            {
                states[i] = MiningBarState.Present;
                scores[i] = candidates[candidate].Score;
                continue;
            }
            var center = centers[i];
            // Rejection by the full-shape matcher is not proof that a bar is gone.
            if (HasBarFragment(fragments, center.X + offsetX, center.Y + offsetY, radius, geometry, settings.BarGap,
                ((previous?.AnchorSlots ?? 0) & (1 << i)) != 0)) continue;
            var rim = MiningCircleMask.Locate(image, center.X + offsetX, center.Y + offsetY, radius, geometry);
            states[i] = rim.Confidence >= 8 ? MiningBarState.Absent : MiningBarState.Unknown;
        }
        return new(states, offsetX, offsetY)
        {
            BarScores = scores,
            HasAnchor = assignments.Count > 0 || previous?.HasAnchor == true,
            AnchorSlots = assignments.Count > 0
                ? assignments.Keys.Aggregate(0, (mask, slot) => mask | (1 << slot))
                    | Enumerable.Range(0, 6).Where(i => states[i] == MiningBarState.Unknown)
                        .Aggregate(0, (mask, slot) => mask | ((previous?.AnchorSlots ?? 0) & (1 << slot)))
                : previous?.AnchorSlots ?? 0
        };
    }

    private static bool HasBarFragment(List<Group> fragments, double x, double y, double radius,
        MiningHudGeometry geometry, double gap, bool previouslyTracked)
    {
        var curve = MiningBarShape.GuidePoints.Select(point => geometry.Transform(point.X, point.Y, radius))
            .Select(point => (X: x + point.X, Y: y + point.Y + radius * gap)).ToArray();
        foreach (var group in fragments)
        {
            var matching = group.Points.Count(pixel => curve.Any(point =>
                Math.Pow(pixel.X - point.X, 2) + Math.Pow(pixel.Y - point.Y, 2) <= 16));
            var required = previouslyTracked ? 6 : 12;
            var fraction = previouslyTracked ? .5 : .6;
            if (matching >= required && matching >= group.Points.Count * fraction) return true;
        }
        return false;
    }
    private static double ScoreGroup(IFssPixelSource source, double x, double y, double radius,
        MiningHudGeometry geometry, double gap)
    {
        var best = 0d;
        for (var dy = -4; dy <= 4; dy++)
            for (var dx = -3; dx <= 3; dx++)
                foreach (var scale in new[] { .9, 1, 1.1 })
                    foreach (var tilt in new[] { -.1, 0, .1 })
                    {
                        var score = MiningBarShape.Score(source, x + dx, y + dy + radius * gap,
                            radius * scale, geometry, tilt, lowerOnly: true);
                        if (score > best && MiningBarShape.Score(source, x + dx, y + dy + radius * gap,
                            radius * scale, geometry, tilt) >= .55) best = score;
                    }
        return best;
    }

    private sealed record Group(List<(int X, int Y)> Points, int MinX, int MinY, int MaxX, int MaxY);

    private static IEnumerable<Group> FindGroups(ColorMask mask)
    {
        var visited = new bool[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
            for (var x = 0; x < mask.Width; x++)
            {
                if (visited[y * mask.Width + x] || !mask.Matches(x, y)) continue;
                var points = new List<(int X, int Y)> { (x, y) };
                visited[y * mask.Width + x] = true;
                var minX = x; var maxX = x; var minY = y; var maxY = y;
                for (var index = 0; index < points.Count; index++)
                {
                    var point = points[index];
                    // Bridge the small gaps between segments, retaining the larger gaps between rigs.
                    for (var ny = Math.Max(0, point.Y - 3); ny <= Math.Min(mask.Height - 1, point.Y + 3); ny++)
                        for (var nx = Math.Max(0, point.X - 3); nx <= Math.Min(mask.Width - 1, point.X + 3); nx++)
                        {
                            if (visited[ny * mask.Width + nx] || !mask.Matches(nx, ny)) continue;
                            visited[ny * mask.Width + nx] = true;
                            points.Add((nx, ny));
                            minX = Math.Min(minX, nx); maxX = Math.Max(maxX, nx);
                            minY = Math.Min(minY, ny); maxY = Math.Max(maxY, ny);
                        }
                }
                yield return new(points, minX, minY, maxX, maxY);
            }
    }

    private sealed class ColorMask : IFssPixelSource
    {
        private readonly bool[] matches;
        public int Width { get; }
        public int Height { get; }
        public ColorMask(IFssPixelSource source, uint color)
        {
            Width = source.Width; Height = source.Height;
            matches = new bool[Width * Height];
            var target = new FssRgbPixel((byte)(color >> 16), (byte)(color >> 8), (byte)color);
            for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
                    matches[y * Width + x] = MatchesColor(source.GetPixel(x, y), target);
        }
        public bool Matches(int x, int y) => matches[y * Width + x];
        public FssRgbPixel GetPixel(int x, int y) => Matches(x, y) ? new(0, 255, 0) : new(0, 0, 0);
    }

    internal static bool MatchesColor(FssRgbPixel pixel, FssRgbPixel target)
    {
        var maximum = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
        var minimum = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
        var chroma = maximum - minimum;
        var targetMin = Math.Min(target.Red, Math.Min(target.Green, target.Blue));
        var targetMaximum = Math.Max(target.Red, Math.Max(target.Green, target.Blue));
        var targetChroma = targetMaximum - targetMin;
        if (targetMaximum < 96 || targetChroma < 24) return false;
        var minimumSaturation = Math.Max(.25, targetChroma / (double)targetMaximum * .5);
        if (maximum < 96 || chroma < maximum * minimumSaturation) return false;
        return Math.Abs((pixel.Red - minimum) / (double)chroma - (target.Red - targetMin) / (double)targetChroma) <= .3
            && Math.Abs((pixel.Green - minimum) / (double)chroma - (target.Green - targetMin) / (double)targetChroma) <= .3
            && Math.Abs((pixel.Blue - minimum) / (double)chroma - (target.Blue - targetMin) / (double)targetChroma) <= .3;
    }
}
