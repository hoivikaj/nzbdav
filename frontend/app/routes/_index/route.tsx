import { redirect } from "react-router";
import type { Route } from "./+types/route";

export async function loader({ request }: Route.LoaderArgs) {
    return redirect("/overview")
}

export async function action() {
    return new Response("Method Not Allowed", {
        status: 405,
        headers: { Allow: "GET, HEAD" },
    });
}
