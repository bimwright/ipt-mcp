namespace Bimwright.Ipt.Server;

internal static class SendCodeTaskGuide
{
    internal const string Text =
        "Typed tools first (task -> tool; use only tools exposed on this connection):\n" +
        "Dirty documents / references -> inventor_list_open_documents / inventor_get_document_info(references=true)\n" +
        "Occurrence tree / bbox / transforms -> inventor_list_occurrences(fields)\n" +
        "Constraint health -> inventor_list_constraints\n" +
        "Clearance -> inventor_measure_min_distance(pairs or set_a/set_b)\n" +
        "Clashes / intersection volumes -> inventor_check_interference\n" +
        "Visibility / grounding / pose -> inventor_set_occurrence_state\n" +
        "Occurrence or part color -> inventor_set_appearance\n" +
        "Named review view -> inventor_create_design_view (Inventor 2027)\n" +
        "Save document / dirty references -> inventor_save_document / inventor_save_all(dry_run=true)\n" +
        "Open silently -> inventor_open_document(silent=true)\n" +
        "Per-body SAT export + read-back check -> inventor_export_bodies_sat(items, out_dir, verify=true)\n" +
        "Many boxes / tubes / extruded profiles in a part (new, join, cut) -> inventor_build_solids(items)\n" +
        "Script inputs (coordinates, paths, member lists) -> send_code data, not inlined source\n" +
        "A response may include one advisory routing_hint{tool,args_sketch,reason}, also on errors; " +
        "it never executes a tool or changes result. Fill argument templates and verify scope; " +
        "a hint can cover only one step. Scripts using modules receive no routing hint. ";
}
