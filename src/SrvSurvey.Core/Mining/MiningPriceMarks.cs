namespace SrvSurvey.Core.Mining;

public static class MiningPriceMarks
{
    public static string For(long price, long averageSellPrice)
    {
        if (price <= 0 || averageSellPrice <= 0)
        {
            return "";
        }

        double percentage = (price / (double)averageSellPrice - 1) * 100;
        if (percentage >= 125)
        {
            return "+++++";
        }

        if (percentage >= 100)
        {
            return "++++";
        }

        if (percentage >= 75)
        {
            return "+++";
        }

        if (percentage >= 50)
        {
            return "++";
        }

        if (percentage >= 25)
        {
            return "+";
        }

        if (percentage >= -5)
        {
            return "";
        }

        if (percentage >= -25)
        {
            return "-";
        }

        if (percentage >= -50)
        {
            return "--";
        }

        return "---";
    }
}
