import type { Route } from "./+types/route";
import { isAuthenticated, login } from "~/auth/authentication.server";
import { Form, redirect, useNavigation } from "react-router";
import { backendClient } from "~/clients/backend-client.server";
import { Alert, Button, Input, Spinner } from "~/components/ui";

type LoginPageData = {
    loginError: string
}

export async function loader({ request }: Route.LoaderArgs) {
    if (await isAuthenticated(request)) return redirect("/");

    const isOnboarding = await backendClient.isOnboarding();
    if (isOnboarding) return redirect("/onboarding");

    return { loginError: null };
}

export default function Index({ loaderData, actionData }: Route.ComponentProps) {
    const navigation = useNavigation();
    const isLoading = navigation.state == "submitting";
    const pageData = actionData || loaderData;
    const showError = !!pageData.loginError;
    const submitButtonDisabled = isLoading;
    const submitButtonText = isLoading ? "Logging in..." : "Login";

    return (
        <main className="hero min-h-dvh bg-base-300">
            <div className="hero-content w-full max-w-sm px-4 py-8">
                <Form
                    className="card w-full border border-base-content/10 bg-base-100 shadow-xl"
                    method="POST"
                >
                    <div className="card-body gap-5">
                        <div className="flex flex-col items-center gap-3 text-center">
                            <img className="h-16 w-16" src="/logo.svg" alt="NzbDAV" />
                            <div>
                                <h1 className="text-2xl font-bold tracking-tight">NzbDAV</h1>
                                <p className="mt-1 text-sm text-base-content/60">Sign in to manage your server</p>
                            </div>
                        </div>

                        {showError && <Alert variant="danger">{pageData.loginError}</Alert>}

                        <fieldset className="fieldset space-y-3">
                            <label className="floating-label">
                                <span>Username</span>
                                <Input
                                    className="w-full"
                                    name="username"
                                    type="text"
                                    placeholder="Enter your username"
                                    autoComplete="username"
                                    autoFocus
                                />
                            </label>
                            <label className="floating-label">
                                <span>Password</span>
                                <Input
                                    className="w-full"
                                    name="password"
                                    type="password"
                                    placeholder="Enter your password"
                                    autoComplete="current-password"
                                />
                            </label>
                        </fieldset>

                        <Button
                            className="w-full"
                            type="submit"
                            size="medium"
                            variant="primary"
                            disabled={submitButtonDisabled}
                        >
                            {isLoading && <Spinner />}
                            {submitButtonText}
                        </Button>
                    </div>
                </Form>
            </div>
        </main>
    );
}

export async function action({ request }: Route.ActionArgs) {
    try {
        const responseInit = await login(request);
        return redirect("/", responseInit);
    }
    catch (error) {
        if (error instanceof Error) {
            return { loginError: error.message };
        }
        throw error;
    }
}
