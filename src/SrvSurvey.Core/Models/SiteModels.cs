using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Models;

public record Beacon(string Name, double X, double Y, double Z, string System);
// ... (other models: Ruin, Structure, Settlement, etc.)