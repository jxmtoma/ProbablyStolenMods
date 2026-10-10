namespace RealTimeShop;
internal static class CalendarText {
 internal static string Append(string native,string clock,bool rich)=>native+"\n"+(rich?"<size=65%>"+clock+"</size>":clock);
}
