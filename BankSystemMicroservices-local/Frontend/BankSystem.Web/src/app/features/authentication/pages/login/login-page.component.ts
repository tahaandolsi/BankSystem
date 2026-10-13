import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';

import { AuthService } from '../../../../core/auth/auth.service';
import { LoginRequest } from '../../models/auth.models';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
})
export class LoginPageComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly isSubmitting = signal(false);
  readonly serverError = signal<string | null>(null);
  readonly form = this.formBuilder.nonNullable.group({
    userName: ['', [Validators.required, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(100)]],
  });

  submit(): void {
    this.serverError.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    const request: LoginRequest = this.form.getRawValue();

    this.authService
      .login(request)
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: () => void this.router.navigateByUrl('/dashboard'),
        error: (error) => this.serverError.set(error.error?.detail ?? 'La connexion a échoué. Réessaie plus tard.'),
      });
  }

  hasError(controlName: 'userName' | 'password', errorName: string): boolean {
    const control = this.form.controls[controlName];
    return control.touched && control.hasError(errorName);
  }
}
