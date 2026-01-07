/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ParameterDto } from './ParameterDto';
export type ProductDetailDto = {
    productId: number | string;
    productName: string;
    productDescription: string | null;
    brandId: number | string | null;
    brandName: string | null;
    categoryId: number | string | null;
    parameters: Array<ParameterDto>;
};

